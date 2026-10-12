using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Admin;

namespace EggIncognito.Services.Feed;

public sealed class FeedDispatcher(
    IFeedSubscriptionStore store,
    FeedSender sender,
    ILogger<FeedDispatcher> logger,
    TimeProvider time,
    AdminNotifier? notifier = null) {
    private const int DeadAfterFailures = 5;

    public const string DefaultPageBaseUrl = "https://eggincognito.egginc.tools";

    public static string BuildPageUrl(string? baseUrl, string platform, string build) =>
        $"{(string.IsNullOrEmpty(baseUrl) ? DefaultPageBaseUrl : baseUrl.TrimEnd('/'))}/protos/{platform}/{build}";

    public async Task DispatchAsync(INotificationEvent evt, CancellationToken ct = default) {
        var kind = FeedEventKinds.For(evt);
        var subs = await store.ActiveAsync(ct);
        foreach (var sub in subs) {
            if (!string.Equals(FeedEventKinds.Normalize(sub.EventKind), kind.Key, StringComparison.Ordinal))
                continue;
            if (await store.AlreadyDeliveredAsync(sub.Id, kind.Key, evt.DedupKey, ct)) continue;

            var result = kind.Evaluate(evt, kind.FilterOf(sub));
            if (result.Ignored.Count > 0)
                logger.LogWarning("feed sub {Id}: ignored filter conditions {Ignored}", sub.Id, kind.Describe(result.Ignored));
            if (!result.Matched) {
                string reason = kind.Describe(result.Failed);
                logger.LogInformation("feed sub {Id}: {Summary} filtered out: {Reason}", sub.Id, evt.Summary, reason);
                await store.SuppressAsync(sub.Id, kind.Key, evt.DedupKey, reason, evt.Summary, ct);
                continue;
            }

            int? code = null;
            bool ok = false;
            try {
                var sent = await sender.SendAsync(sub, DiscordFeedPayload.Build(evt, sub.MessageTemplate), ct);
                code = sent.Code;
                ok = sent.Ok;
                if (sent.Dead) await DeactivateAsync(sub.Id, ct);
            } catch (Exception ex) {
                logger.LogWarning(ex, "feed dispatch to sub {Id} threw", sub.Id);
            }

            await store.RecordAsync(new FeedDelivery {
                SubscriptionId = sub.Id,
                EventKind = kind.Key,
                DedupKey = evt.DedupKey,
                Summary = evt.Summary,
                Status = ok ? "sent" : "failed",
                AttemptedAt = time.GetUtcNow(),
                ResponseCode = code,
                Attempts = 1
            }, ct);

            if (ok) {
                await store.MarkDeliveredAsync(sub.Id, time.GetUtcNow(), ct);
            } else {
                await store.BumpFailAsync(sub.Id, ct);
                var refreshed = (await store.ActiveAsync(ct)).Find(s => s.Id == sub.Id);
                if (refreshed is not null && refreshed.FailCount >= DeadAfterFailures)
                    await DeactivateAsync(sub.Id, ct);
            }
        }
    }

    private async Task DeactivateAsync(int subId, CancellationToken ct) {
        await store.SetActiveAsync(subId, false, ct);
        notifier?.Publish(AdminTopics.Notifications);
    }
}
