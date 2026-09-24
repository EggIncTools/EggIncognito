using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class ApiKeyStore(EggIncognitoDbContext db, TimeProvider time) {
    public async Task<ApiKey> AddAsync(Guid owner, string name, string hash, string prefix,
        CancellationToken ct = default) {
        var row = new ApiKey {
            OwnerUserId = owner,
            Name = string.IsNullOrWhiteSpace(name) ? "key" : name.Trim(),
            KeyHash = hash,
            Prefix = prefix
        };
        db.ApiKeys.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<IReadOnlyList<ApiKey>> ByOwnerAsync(Guid owner, CancellationToken ct = default) =>
        await db.ApiKeys.AsNoTracking()
            .Where(k => k.OwnerUserId == owner)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

    public async Task<int> ActiveCountAsync(Guid owner, CancellationToken ct = default) =>
        await db.ApiKeys.CountAsync(k => k.OwnerUserId == owner && !k.Revoked, ct);

    public async Task<bool> RevokeAsync(int id, Guid owner, CancellationToken ct = default) {
        var row = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.OwnerUserId == owner, ct);
        if (row is null || row.Revoked) return false;
        row.Revoked = true;
        row.RevokedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ApiKey?> FindActiveByHashAsync(string hash, CancellationToken ct = default) =>
        await db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash && !k.Revoked, ct);

    public async Task TouchAsync(int id, CancellationToken ct = default) {
        var row = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (row is null) return;
        var now = time.GetUtcNow();
        row.RequestCount++;
        if (row.LastUsedAt is null || now - row.LastUsedAt.Value > TimeSpan.FromSeconds(60))
            row.LastUsedAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ApiKey>> AllAsync(CancellationToken ct = default) =>
        await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync(ct);

    public async Task<bool> AdminRevokeAsync(int id, CancellationToken ct = default) {
        var row = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (row is null || row.Revoked) return false;
        row.Revoked = true;
        row.RevokedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> AdminDeleteAsync(int id, CancellationToken ct = default) {
        var row = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (row is null) return false;
        db.ApiKeys.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
