using EggIncognito.Core;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed record DeviceAssetHead(string Platform, string Kind, string Name, string Sha256, long ByteSize,
    string ContentType, string? SourceVersion, DateTimeOffset UpdatedAt);

public sealed class DeviceAssetStore(EggIncognitoDbContext db, BlobBytes blobs) {
    public Task<byte[]> BytesAsync(DeviceAsset row, CancellationToken ct) =>
        blobs.ResolveAsync(BlobTables.DeviceAssets, row.Bytes, row.Sha256, ct);

    public const string FingerprintPrefix = "fp:";

    public async Task<DeviceAsset?> GetAsync(string kind, string name, string? platform, CancellationToken ct) {
        var q = db.DeviceAssets.AsNoTracking().Where(a => a.Kind == kind && a.Name == name);
        if (!string.IsNullOrEmpty(platform)) {
            var exact = await q.FirstOrDefaultAsync(a => a.Platform == platform, ct);
            if (exact is not null) return exact;
        }

        return await q.OrderByDescending(a => a.UpdatedAt).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<DeviceAssetHead>> ListAsync(string kind, string? platform, CancellationToken ct) {
        var q = db.DeviceAssets.AsNoTracking().Where(a => a.Kind == kind);
        if (!string.IsNullOrEmpty(platform)) q = q.Where(a => a.Platform == platform);
        return await q.OrderBy(a => a.Name)
            .Select(a => new DeviceAssetHead(a.Platform, a.Kind, a.Name, a.Sha256, a.ByteSize, a.ContentType,
                a.SourceVersion, a.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, string>> ShaManifestAsync(string platform, string kind,
        CancellationToken ct) {
        var rows = await db.DeviceAssets.AsNoTracking()
            .Where(a => a.Platform == platform && a.Kind == kind)
            .Select(a => new { a.Name, a.Sha256 })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Name, r => r.Sha256, StringComparer.Ordinal);
    }

    public async Task<bool> PutAsync(string platform, string kind, string name, byte[] bytes, string contentType,
        string? sourceVersion, CancellationToken ct) {
        string sha = Hashes.Sha256Hex(bytes);
        var existing = await db.DeviceAssets
            .FirstOrDefaultAsync(a => a.Platform == platform && a.Kind == kind && a.Name == name, ct);
        if (existing is not null && string.Equals(existing.Sha256, sha, StringComparison.Ordinal)) return false;

        var stored = await blobs.StoreAsync(BlobTables.DeviceAssets, sha, bytes, ct);

        if (existing is null) {
            db.DeviceAssets.Add(new DeviceAsset {
                Platform = platform,
                Kind = kind,
                Name = name,
                Sha256 = sha,
                Bytes = stored,
                ByteSize = bytes.LongLength,
                ContentType = contentType,
                SourceVersion = sourceVersion,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        } else {
            existing.Sha256 = sha;
            existing.Bytes = stored;
            existing.ByteSize = bytes.LongLength;
            existing.ContentType = contentType;
            existing.SourceVersion = sourceVersion;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> PruneAsync(string platform, string kind, IReadOnlyCollection<string> keep,
        CancellationToken ct) {
        var doomed = await db.DeviceAssets
            .Where(a => a.Platform == platform && a.Kind == kind && !keep.Contains(a.Name)
                        && !a.Name.StartsWith(FingerprintPrefix))
            .ToListAsync(ct);
        if (doomed.Count == 0) return 0;
        db.DeviceAssets.RemoveRange(doomed);
        await db.SaveChangesAsync(ct);
        return doomed.Count;
    }
}
