using System.Text;
using EggIncognito.Core.Services;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class DbEndpointSource(EggIncognitoDbContext db) : IEndpointSource {
    public int Priority => 100;

    public byte[]? Lookup(string path, string? eid) => TryLookupWithStamp(path, eid).Bytes;

    public (byte[]? Bytes, DateTimeOffset? Stamp) TryLookupWithStamp(string path, string? eid) {
        string cleanPath = path.TrimEnd('/');
        while (true) {
            var hit = db.StoredEndpoints.AsNoTracking()
                .Where(e => e.Path == cleanPath && (e.Eid == eid || e.Eid == null))
                .OrderBy(e => e.Eid == null ? 1 : 0)
                .Select(e => new { e.ResponseJson, e.UpdatedAt })
                .FirstOrDefault();
            if (hit is not null) return (Encoding.UTF8.GetBytes(hit.ResponseJson), hit.UpdatedAt);

            int lastSlash = cleanPath.LastIndexOf('/');
            int firstSlash = cleanPath.IndexOf('/');
            if (lastSlash <= firstSlash) break;
            cleanPath = cleanPath[..lastSlash];
        }

        return (null, null);
    }

    public async Task<string?> UpsertAsync(string path, string? eid, string responseJson, string responseType,
        Guid? ownerUserId, DateTimeOffset now, CancellationToken ct = default) {
        var existing = await db.StoredEndpoints
            .FirstOrDefaultAsync(e => e.Path == path && e.Eid == eid, ct);
        string? previousJson = existing?.ResponseJson;
        if (existing is null) {
            db.StoredEndpoints.Add(new StoredEndpoint {
                Path = path,
                Eid = eid,
                ResponseJson = responseJson,
                ResponseType = responseType,
                OwnerUserId = ownerUserId
            });
        } else {
            existing.ResponseJson = responseJson;
            existing.ResponseType = responseType;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return previousJson;
    }
}
