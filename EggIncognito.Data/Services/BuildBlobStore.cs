using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed record BuildBlobHead(string Key, string Source, string Sha256, long ByteSize, DateTimeOffset FetchedAt);

public sealed class BuildBlobStore(EggIncognitoDbContext db, TimeProvider time, BlobBytes blobs) {
    public Task<byte[]> BytesAsync(BuildBlob row, CancellationToken ct) =>
        blobs.ResolveAsync(BlobTables.BuildBlobs, row.Bytes, row.Sha256, ct);

    public Task<BuildBlob?> GetAsync(string key, CancellationToken ct) =>
        db.BuildBlobs.AsNoTracking().FirstOrDefaultAsync(b => b.Key == key, ct);

    public async Task PutAsync(string key, string source, string sha256, byte[] bytes, CancellationToken ct) {
        var stored = await blobs.StoreAsync(BlobTables.BuildBlobs, sha256, bytes, ct);
        var existing = await db.BuildBlobs.FirstOrDefaultAsync(b => b.Key == key, ct);
        if (existing is null) {
            db.BuildBlobs.Add(new BuildBlob {
                Key = key,
                Source = source,
                Sha256 = sha256,
                ByteSize = bytes.LongLength,
                Bytes = stored,
                FetchedAt = time.GetUtcNow()
            });
        } else {
            existing.Source = source;
            existing.Sha256 = sha256;
            existing.ByteSize = bytes.LongLength;
            existing.Bytes = stored;
            existing.FetchedAt = time.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
    }

    public Task<List<BuildBlobHead>> ListAsync(CancellationToken ct) =>
        db.BuildBlobs.AsNoTracking()
            .OrderBy(b => b.Key)
            .Select(b => new BuildBlobHead(b.Key, b.Source, b.Sha256, b.ByteSize, b.FetchedAt))
            .ToListAsync(ct);
}
