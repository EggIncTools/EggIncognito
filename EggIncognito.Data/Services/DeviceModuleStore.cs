using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed record StoredModuleHead(
    string Name, string Source, string? Version, string Sha256, long ByteSize, DateTimeOffset FetchedAt);

public sealed class DeviceModuleStore(EggIncognitoDbContext db, TimeProvider time, BlobBytes blobs) {
    public Task<byte[]> BytesAsync(StoredModule row, CancellationToken ct) =>
        blobs.ResolveAsync(BlobTables.DeviceModules, row.Bytes, row.Sha256, ct);

    public async Task PutAsync(
        string name, string source, string? version, string sha256, byte[] bytes, CancellationToken ct) {
        var stored = await blobs.StoreAsync(BlobTables.DeviceModules, sha256, bytes, ct);
        var existing = await db.DeviceModules.FirstOrDefaultAsync(m => m.Name == name, ct);
        if (existing is null) {
            db.DeviceModules.Add(new StoredModule {
                Name = name,
                Source = source,
                Version = version,
                Sha256 = sha256,
                Bytes = stored,
                ByteSize = bytes.LongLength,
                FetchedAt = time.GetUtcNow()
            });
        } else {
            existing.Source = source;
            existing.Version = version;
            existing.Sha256 = sha256;
            existing.Bytes = stored;
            existing.ByteSize = bytes.LongLength;
            existing.FetchedAt = time.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
    }

    public Task<StoredModule?> LatestAsync(string name, CancellationToken ct) =>
        db.DeviceModules.AsNoTracking()
            .Where(m => m.Name == name)
            .OrderByDescending(m => m.FetchedAt)
            .FirstOrDefaultAsync(ct);

    public Task<List<StoredModuleHead>> ListAsync(CancellationToken ct) =>
        db.DeviceModules.AsNoTracking()
            .OrderBy(m => m.Name)
            .Select(m => new StoredModuleHead(m.Name, m.Source, m.Version, m.Sha256, m.ByteSize, m.FetchedAt))
            .ToListAsync(ct);
}
