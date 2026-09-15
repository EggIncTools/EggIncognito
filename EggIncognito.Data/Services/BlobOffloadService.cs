using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Data.Services;

public sealed record BlobOffloadProgress(string Table, int Moved, int Verified, int Failed, long BytesMoved);

public sealed class BlobOffloadService(
    IServiceScopeFactory scopes,
    BlobFileStore files,
    BlobOffloadGate gate,
    ILogger<BlobOffloadService> logger) : BackgroundService {
    private const int BatchSize = 16;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            await AuditAsync(stoppingToken);
            if (!gate.Enabled) {
                await ReportPendingAsync(stoppingToken);
                return;
            }

            foreach (var move in Movers) {
                if (stoppingToken.IsCancellationRequested) return;
                var result = await move(this, stoppingToken);
                if (result.Moved > 0 || result.Failed > 0) {
                    logger.LogInformation(
                        "blob offload {Table}: moved {Moved} ({Bytes} bytes), failed {Failed}",
                        result.Table, result.Moved, result.BytesMoved, result.Failed);
                }
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
        } catch (Exception ex) {
            logger.LogError(ex, "blob offload failed; rows remain in Postgres and reads still work");
        }
    }

    private static readonly Func<BlobOffloadService, CancellationToken, Task<BlobOffloadProgress>>[] Movers = [
        (s, ct) => s.MoveAsync<DeviceAsset>(BlobTables.DeviceAssets, ct),
        (s, ct) => s.MoveAsync<BuildBlob>(BlobTables.BuildBlobs, ct),
        (s, ct) => s.MoveAsync<StoredBinary>(BlobTables.StoredBinaries, ct),
        (s, ct) => s.MoveAsync<StoredApk>(BlobTables.StoredApks, ct),
        (s, ct) => s.MoveAsync<StoredModule>(BlobTables.DeviceModules, ct)
    ];

    private async Task<BlobOffloadProgress> MoveAsync<T>(string table, CancellationToken ct) where T : class, IBlobRow {
        int moved = 0, failed = 0;
        long bytesMoved = 0;

        while (!ct.IsCancellationRequested) {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();

            var batch = await db.Set<T>().Where(r => r.Bytes != null).Take(BatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;

            bool progressed = false;
            foreach (var row in batch) {
                if (row.Bytes is not { } bytes) continue;
                if (string.IsNullOrWhiteSpace(row.Sha256)) {
                    logger.LogWarning("blob offload {Table} id {Id}: no sha256, left in Postgres", table, row.Id);
                    failed++;
                    continue;
                }

                await files.WriteAsync(table, row.Sha256, bytes, ct);
                if (!await files.VerifyAsync(table, row.Sha256, ct)) {
                    logger.LogError(
                        "blob offload {Table} id {Id}: sha mismatch after write, left in Postgres", table, row.Id);
                    failed++;
                    continue;
                }

                row.Bytes = null;
                moved++;
                bytesMoved += bytes.LongLength;
                progressed = true;
            }

            await db.SaveChangesAsync(ct);
            if (!progressed) break;
        }

        return new BlobOffloadProgress(table, moved, 0, failed, bytesMoved);
    }

    private async Task ReportPendingAsync(CancellationToken ct) {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();

        long pending = 0, bytes = 0;
        foreach (var counted in new[] {
            await PendingAsync<DeviceAsset>(db, ct),
            await PendingAsync<BuildBlob>(db, ct),
            await PendingAsync<StoredBinary>(db, ct),
            await PendingAsync<StoredApk>(db, ct),
            await PendingAsync<StoredModule>(db, ct)
        }) {
            pending += counted.Rows;
            bytes += counted.Bytes;
        }

        if (pending == 0) return;
        logger.LogWarning(
            "blob offload is DISABLED and {Pending} row(s) totalling {Bytes} bytes are still in Postgres. "
            + "Set {Key}=true to move them to {Root}.", pending, bytes, BlobOffloadGate.SettingKey, files.Root);
    }

    private static async Task<(long Rows, long Bytes)> PendingAsync<T>(EggIncognitoDbContext db, CancellationToken ct)
        where T : class, IBlobRow {
        var q = db.Set<T>().AsNoTracking().Where(r => r.Bytes != null);
        return (await q.LongCountAsync(ct), await q.SumAsync(r => r.ByteSize, ct));
    }

    private async Task AuditAsync(CancellationToken ct) {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();

        int missing = 0;
        missing += await MissingAsync<DeviceAsset>(db, BlobTables.DeviceAssets, ct);
        missing += await MissingAsync<BuildBlob>(db, BlobTables.BuildBlobs, ct);
        missing += await MissingAsync<StoredBinary>(db, BlobTables.StoredBinaries, ct);
        missing += await MissingAsync<StoredApk>(db, BlobTables.StoredApks, ct);
        missing += await MissingAsync<StoredModule>(db, BlobTables.DeviceModules, ct);

        if (missing > 0) {
            logger.LogError(
                "blob audit: {Missing} row(s) have no bytes in Postgres and no file on disk under {Root}. " +
                "Reads of those rows will throw. Restore the files or re-harvest.", missing, files.Root);
        }
    }

    private async Task<int> MissingAsync<T>(EggIncognitoDbContext db, string table, CancellationToken ct)
        where T : class, IBlobRow {
        var offloaded = await db.Set<T>().AsNoTracking()
            .Where(r => r.Bytes == null)
            .Select(r => new { r.Id, r.Sha256 })
            .ToListAsync(ct);

        int missing = 0;
        foreach (var row in offloaded) {
            if (files.Exists(table, row.Sha256)) continue;
            logger.LogError("blob audit {Table} id {Id}: file missing for sha {Sha}", table, row.Id, row.Sha256);
            missing++;
        }

        return missing;
    }
}
