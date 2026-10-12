using EggIncognito.Data.Models;
using EggIncognito.Models.Notifications;

namespace EggIncognito.Services.Feed;

public static class FeedAdminGrouping {
    public const string Unowned = "unowned";

    public static AdminFeedListResponse Build(
        IReadOnlyList<FeedSubscription> subs, IReadOnlyDictionary<Guid, string> usernames) {
        var rows = subs
            .Select(s => new AdminFeedRow(
                s.Id,
                s.OwnerUserId,
                s.OwnerUserId is { } uid && usernames.TryGetValue(uid, out string? name) ? name : Unowned,
                FeedEventKinds.Normalize(s.EventKind),
                s.Kind,
                s.Label,
                WebhookMask.Mask(s.TargetUrl),
                EggIdentity.Filters.FilterCodec.Serialize(
                    (FeedEventKinds.Find(FeedEventKinds.Normalize(s.EventKind)) ?? FeedEventKinds.Proto).FilterOf(s)),
                s.Active,
                s.CreatedAt,
                s.LastDeliveryAt,
                s.FailCount))
            .OrderBy(r => r.OwnerUsername == Unowned)
            .ThenBy(r => r.OwnerUsername, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(r => r.CreatedAt)
            .ToList();
        int owners = rows.Select(r => r.OwnerUsername).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return new AdminFeedListResponse(rows.Count, rows.Count(r => r.Active), owners, rows);
    }
}
