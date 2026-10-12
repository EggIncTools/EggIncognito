using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class ApiKeyFeatureStore(EggIncognitoDbContext db, TimeProvider time) {
    public async Task<ApiKeyFeature?> RequestAsync(int keyId, Guid owner, string feature, string? note,
        CancellationToken ct = default) {
        bool owned = await db.ApiKeys.AnyAsync(k => k.Id == keyId && k.OwnerUserId == owner && !k.Revoked, ct);
        if (!owned) return null;

        var row = await db.ApiKeyFeatures.FirstOrDefaultAsync(f => f.ApiKeyId == keyId && f.Feature == feature, ct);
        if (row is null) {
            row = new ApiKeyFeature { ApiKeyId = keyId, Feature = feature };
            db.ApiKeyFeatures.Add(row);
        } else if (!ApiKeyFeatureStates.Requestable(row.State)) {
            return row;
        }

        row.State = ApiKeyFeatureStates.Pending;
        row.Note = string.IsNullOrWhiteSpace(note) ? null : note;
        row.RequestedAt = time.GetUtcNow();
        row.DecidedAt = null;
        row.DecidedBy = null;
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<string?> StateAsync(int keyId, string feature, CancellationToken ct = default) =>
        await db.ApiKeyFeatures.AsNoTracking()
            .Where(f => f.ApiKeyId == keyId && f.Feature == feature)
            .Select(f => f.State)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ApiKeyFeature>> ForKeysAsync(IReadOnlyCollection<int> keyIds,
        CancellationToken ct = default) =>
        await db.ApiKeyFeatures.AsNoTracking().Where(f => keyIds.Contains(f.ApiKeyId)).ToListAsync(ct);

    public async Task<IReadOnlyList<ApiKeyFeature>> AllAsync(CancellationToken ct = default) =>
        await db.ApiKeyFeatures.AsNoTracking().OrderByDescending(f => f.RequestedAt).ToListAsync(ct);

    public async Task<(GrantDecision Outcome, ApiKeyFeature? Row)> DecideAsync(int grantId, string target, string by,
        CancellationToken ct = default) {
        var row = await db.ApiKeyFeatures.FirstOrDefaultAsync(f => f.Id == grantId, ct);
        if (row is null) return (GrantDecision.NotFound, null);
        if (!ApiKeyFeatureStates.CanDecide(row.State, target)) return (GrantDecision.Invalid, row);
        row.State = target;
        row.DecidedAt = time.GetUtcNow();
        row.DecidedBy = by;
        await db.SaveChangesAsync(ct);
        return (GrantDecision.Done, row);
    }
}
