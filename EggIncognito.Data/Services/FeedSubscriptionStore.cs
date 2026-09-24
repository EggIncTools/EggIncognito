using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public interface IFeedSubscriptionStore {
    Task<FeedSubscription> AddAsync(FeedSubscription sub, CancellationToken ct = default);
    Task<List<FeedSubscription>> ActiveAsync(CancellationToken ct = default);
    Task<bool> AlreadyDeliveredAsync(int subId, string eventKind, string dedupKey, CancellationToken ct = default);
    Task RecordAsync(FeedDelivery delivery, CancellationToken ct = default);
    Task SetActiveAsync(int subId, bool active, CancellationToken ct = default);
    Task BumpFailAsync(int subId, CancellationToken ct = default);
    Task MarkDeliveredAsync(int subId, DateTimeOffset at, CancellationToken ct = default);
    Task<List<FeedSubscription>> ByOwnerAsync(Guid ownerUserId, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, Guid ownerUserId, CancellationToken ct = default);

    Task<bool> UpdateAsync(int id, Guid ownerUserId, string[] platforms, string trigger, bool active,
        string? messageTemplate, string[] filters, CancellationToken ct = default);

    Task SuppressAsync(int subId, string eventKind, string dedupKey, string reason, string? summary,
        CancellationToken ct = default);

    Task<List<FeedSubscription>> AllForAdminAsync(CancellationToken ct = default);
    Task<FeedSubscription?> AdminByIdAsync(int id, CancellationToken ct = default);
    Task<bool> AdminDeactivateAsync(int id, CancellationToken ct = default);
    Task<bool> AdminDeleteAsync(int id, CancellationToken ct = default);
}

public sealed class FeedSubscriptionStore(EggIncognitoDbContext db, TimeProvider time) : IFeedSubscriptionStore {
    public async Task<FeedSubscription> AddAsync(FeedSubscription sub, CancellationToken ct = default) {
        db.FeedSubscriptions.Add(sub);
        await db.SaveChangesAsync(ct);
        return sub;
    }

    public Task<List<FeedSubscription>> ActiveAsync(CancellationToken ct = default) =>
        db.FeedSubscriptions.AsNoTracking().Where(s => s.Active).ToListAsync(ct);

    public async Task<bool> AlreadyDeliveredAsync(int subId, string eventKind, string dedupKey,
        CancellationToken ct = default) =>
        await db.FeedDeliveries.AnyAsync(
            d => d.SubscriptionId == subId && d.EventKind == eventKind && d.DedupKey == dedupKey, ct);

    public async Task RecordAsync(FeedDelivery delivery, CancellationToken ct = default) {
        db.FeedDeliveries.Add(delivery);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(int subId, bool active, CancellationToken ct = default) {
        var s = await db.FeedSubscriptions.FirstOrDefaultAsync(x => x.Id == subId, ct);
        if (s is null) return;
        s.Active = active;
        await db.SaveChangesAsync(ct);
    }

    public async Task BumpFailAsync(int subId, CancellationToken ct = default) {
        var s = await db.FeedSubscriptions.FirstOrDefaultAsync(x => x.Id == subId, ct);
        if (s is null) return;
        s.FailCount++;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkDeliveredAsync(int subId, DateTimeOffset at, CancellationToken ct = default) {
        var s = await db.FeedSubscriptions.FirstOrDefaultAsync(x => x.Id == subId, ct);
        if (s is null) return;
        s.LastDeliveryAt = at;
        s.FailCount = 0;
        await db.SaveChangesAsync(ct);
    }

    public Task<List<FeedSubscription>> ByOwnerAsync(Guid ownerUserId, CancellationToken ct = default) =>
        db.FeedSubscriptions.AsNoTracking()
            .Where(s => s.OwnerUserId == ownerUserId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<bool> DeleteAsync(int id, Guid ownerUserId, CancellationToken ct = default) =>
        await db.FeedSubscriptions
            .Where(s => s.Id == id && s.OwnerUserId == ownerUserId)
            .ExecuteDeleteAsync(ct) > 0;

    public async Task<bool> UpdateAsync(
        int id, Guid ownerUserId, string[] platforms, string trigger, bool active, string? messageTemplate,
        string[] filters, CancellationToken ct = default) {
        var row = await db.FeedSubscriptions.FirstOrDefaultAsync(s => s.Id == id && s.OwnerUserId == ownerUserId, ct);
        if (row is null) return false;
        row.Platforms = platforms is { Length: > 0 } ? platforms : ["android", "ios"];
        row.Trigger = string.IsNullOrWhiteSpace(trigger) ? row.Trigger : trigger;
        row.Active = active;
        row.MessageTemplate = string.IsNullOrWhiteSpace(messageTemplate) ? null : messageTemplate;
        row.Filters = filters;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private const int SuppressionsKeptPerSub = 50;

    public async Task SuppressAsync(
        int subId, string eventKind, string dedupKey, string reason, string? summary,
        CancellationToken ct = default) {
        var latest = await db.FeedSuppressions.AsNoTracking()
            .Where(s => s.SubscriptionId == subId && s.EventKind == eventKind && s.DedupKey == dedupKey)
            .OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct);
        if (latest is not null && latest.Reason == reason) return;

        db.FeedSuppressions.Add(new FeedSuppression {
            SubscriptionId = subId,
            EventKind = eventKind,
            DedupKey = dedupKey,
            Reason = reason,
            Summary = summary,
            CreatedAt = time.GetUtcNow()
        });
        await db.SaveChangesAsync(ct);

        var stale = await db.FeedSuppressions
            .Where(s => s.SubscriptionId == subId)
            .OrderByDescending(s => s.Id)
            .Skip(SuppressionsKeptPerSub)
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (stale.Count == 0) return;
        await db.FeedSuppressions.Where(s => stale.Contains(s.Id)).ExecuteDeleteAsync(ct);
    }

    public Task<List<FeedDelivery>> DeliveriesAsync(int subId, int take, CancellationToken ct = default) =>
        db.FeedDeliveries.AsNoTracking()
            .Where(d => d.SubscriptionId == subId)
            .OrderByDescending(d => d.Id).Take(take).ToListAsync(ct);

    public Task<List<FeedSuppression>> SuppressionsAsync(int subId, int take, CancellationToken ct = default) =>
        db.FeedSuppressions.AsNoTracking()
            .Where(s => s.SubscriptionId == subId)
            .OrderByDescending(s => s.Id).Take(take).ToListAsync(ct);

    public Task<List<FeedSubscription>> AllForAdminAsync(CancellationToken ct = default) =>
        db.FeedSubscriptions.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public Task<FeedSubscription?> AdminByIdAsync(int id, CancellationToken ct = default) =>
        db.FeedSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<bool> AdminDeactivateAsync(int id, CancellationToken ct = default) {
        var s = await db.FeedSubscriptions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return false;
        s.Active = false;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> AdminDeleteAsync(int id, CancellationToken ct = default) =>
        await db.FeedSubscriptions.Where(s => s.Id == id).ExecuteDeleteAsync(ct) > 0;
}
