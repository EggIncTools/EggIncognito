using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed record SymbolizedBinaryInfo(
    string Platform, string AppVersion, string Sha256, long ByteSize, int SymbolCount, DateTimeOffset UploadedAt);

public sealed class SymbolizedReferenceStore(EggIncognitoDbContext db, TimeProvider time) {
    public Task<SymbolizedBinary?> GetAsync(string platform, string version, CancellationToken ct = default) =>
        db.SymbolizedBinaries.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Platform == platform && b.AppVersion == version, ct);

    public Task<bool> ExistsAsync(string platform, string version, CancellationToken ct = default) =>
        db.SymbolizedBinaries.AsNoTracking()
            .AnyAsync(b => b.Platform == platform && b.AppVersion == version, ct);

    public async Task<bool> DeleteAsync(string platform, string version, CancellationToken ct = default) {
        int removed = await db.SymbolizedBinaries
            .Where(b => b.Platform == platform && b.AppVersion == version)
            .ExecuteDeleteAsync(ct);
        return removed > 0;
    }

    public Task<SymbolizedBinary?> GetLatestAsync(string platform, CancellationToken ct = default) =>
        db.SymbolizedBinaries.AsNoTracking()
            .Where(b => b.Platform == platform)
            .OrderByDescending(b => b.UploadedAt)
            .FirstOrDefaultAsync(ct);

    public async Task PutAsync(string platform, string version, string sha256, byte[] bytes, int symbolCount,
        CancellationToken ct = default) {
        var now = time.GetUtcNow();
        if (await ExistsAsync(platform, version, ct)) {
            await db.SymbolizedBinaries
                .Where(b => b.Platform == platform && b.AppVersion == version)
                .ExecuteUpdateAsync(s => {
                    s.SetProperty(b => b.Sha256, sha256);
                    s.SetProperty(b => b.Bytes, bytes);
                    s.SetProperty(b => b.ByteSize, bytes.LongLength);
                    s.SetProperty(b => b.SymbolCount, symbolCount);
                    s.SetProperty(b => b.UploadedAt, now);
                }, ct);
            return;
        }

        db.SymbolizedBinaries.Add(new SymbolizedBinary {
            Platform = platform,
            AppVersion = version,
            Sha256 = sha256,
            Bytes = bytes,
            ByteSize = bytes.LongLength,
            SymbolCount = symbolCount,
            UploadedAt = now
        });
        await db.SaveChangesAsync(ct);
    }

    public Task<List<SymbolizedBinaryInfo>> ListAsync(CancellationToken ct = default) =>
        db.SymbolizedBinaries.AsNoTracking()
            .OrderByDescending(b => b.UploadedAt)
            .Select(b => new SymbolizedBinaryInfo(b.Platform, b.AppVersion, b.Sha256, b.ByteSize, b.SymbolCount,
                b.UploadedAt))
            .ToListAsync(ct);
}
