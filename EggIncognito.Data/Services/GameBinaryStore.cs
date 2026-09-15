using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed record StoredBinaryInfo(
    string Platform, string AppVersion, string Sha256, long ByteSize, int NativeSymbolCount, int EffectiveSymbolCount,
    string Source, DateTimeOffset PulledAt);

public sealed class GameBinaryStore(EggIncognitoDbContext db, BlobBytes blobs) {
    public Task<byte[]> BytesAsync(StoredBinary row, CancellationToken ct) =>
        blobs.ResolveAsync(BlobTables.StoredBinaries, row.Bytes, row.Sha256, ct);

    public Task<StoredBinary?> GetAsync(string platform, string version, CancellationToken ct = default) =>
        db.StoredBinaries.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Platform == platform && b.AppVersion == version, ct);

    public Task<bool> ExistsAsync(string platform, string version, CancellationToken ct = default) =>
        db.StoredBinaries.AsNoTracking()
            .AnyAsync(b => b.Platform == platform && b.AppVersion == version, ct);

    public async Task<bool> DeleteAsync(string platform, string version, CancellationToken ct = default) {
        int removed = await db.StoredBinaries
            .Where(b => b.Platform == platform && b.AppVersion == version)
            .ExecuteDeleteAsync(ct);
        return removed > 0;
    }

    public Task<StoredBinary?> GetLatestAsync(string platform, CancellationToken ct = default) =>
        db.StoredBinaries.AsNoTracking()
            .Where(b => b.Platform == platform)
            .OrderByDescending(b => b.PulledAt)
            .FirstOrDefaultAsync(ct);

    public async Task PutAsync(string platform, string version, string sha256, byte[] bytes, int nativeSymbolCount,
        int effectiveSymbolCount, string source, CancellationToken ct = default) {
        var stored = await blobs.StoreAsync(BlobTables.StoredBinaries, sha256, bytes, ct);
        var row = await db.StoredBinaries.FirstOrDefaultAsync(b => b.Platform == platform && b.AppVersion == version, ct);
        if (row is null) {
            db.StoredBinaries.Add(new StoredBinary {
                Platform = platform,
                AppVersion = version,
                Sha256 = sha256,
                Bytes = stored,
                ByteSize = bytes.LongLength,
                NativeSymbolCount = nativeSymbolCount,
                EffectiveSymbolCount = effectiveSymbolCount,
                Source = source,
                PulledAt = DateTimeOffset.UtcNow
            });
        } else {
            row.Sha256 = sha256;
            row.Bytes = stored;
            row.ByteSize = bytes.LongLength;
            row.NativeSymbolCount = nativeSymbolCount;
            row.EffectiveSymbolCount = effectiveSymbolCount;
            row.Source = source;
            row.PulledAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public Task<List<StoredBinaryInfo>> ListAsync(CancellationToken ct = default) =>
        db.StoredBinaries.AsNoTracking()
            .OrderByDescending(b => b.PulledAt)
            .Select(b => new StoredBinaryInfo(b.Platform, b.AppVersion, b.Sha256, b.ByteSize, b.NativeSymbolCount,
                b.EffectiveSymbolCount, b.Source, b.PulledAt))
            .ToListAsync(ct);
}
