using System.Text;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Admin;

namespace EggIncognito.Services.Feed;

public sealed class FeedDispatcher(
    IFeedSubscriptionStore store,
    IHttpClientFactory httpFactory,
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
        var http = httpFactory.CreateClient("discord-api");
        foreach (var sub in subs) {
            if (!string.Equals(FeedEventKinds.Normalize(sub.EventKind), kind.Key, StringComparison.Ordinal))
                continue;
            if (!kind.Matches(evt, sub)) continue;
            if (await store.AlreadyDeliveredAsync(sub.Id, kind.Key, evt.DedupKey, ct)) continue;

            if (kind.BlockedBy(evt, sub) is { Count: > 0 } blocked) {
                string reason = string.Join(",", blocked);
                logger.LogInformation("feed sub {Id}: {Summary} suppressed by {Reason}", sub.Id, evt.Summary, reason);
                await store.SuppressAsync(sub.Id, kind.Key, evt.DedupKey, reason, evt.Summary, ct);
                continue;
            }

            int? code = null;
            bool ok = false;
            try {
                string body = DiscordFeedPayload.Build(evt, sub.MessageTemplate);
                var res = await http.PostAsync(sub.TargetUrl,
                    new StringContent(body, Encoding.UTF8, "application/json"), ct);
                code = (int)res.StatusCode;
                ok = res.IsSuccessStatusCode;
                if (code is 404 or 410) await DeactivateAsync(sub.Id, ct);
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
