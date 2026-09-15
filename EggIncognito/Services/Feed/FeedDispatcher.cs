using System.Text;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Admin;

namespace EggIncognito.Services.Feed;

public sealed class FeedDispatcher(
    IFeedSubscriptionStore store,
    IHttpClientFactory httpFactory,
    ILogger<FeedDispatcher> logger,
    AdminNotifier? notifier = null) {
    private const int DeadAfterFailures = 5;

    public const string DefaultPageBaseUrl = "https://eggincognito.egginc.tools";

    public static string BuildPageUrl(string? baseUrl, string platform, string build) =>
        $"{(string.IsNullOrEmpty(baseUrl) ? DefaultPageBaseUrl : baseUrl.TrimEnd('/'))}/protos/{platform}/{build}";

    public async Task DispatchAsync(INotificationEvent evt, CancellationToken ct = default) {
        var subs = await store.ActiveAsync(ct);
        var http = httpFactory.CreateClient("discord-api");
        foreach (var sub in subs) {
            if (!string.Equals(FeedEventKinds.Normalize(sub.EventKind), evt.EventKind, StringComparison.Ordinal))
                continue;
            if (!evt.Matches(sub)) continue;
            if (await store.AlreadyDeliveredAsync(sub.Id, evt.EventKind, evt.DedupKey, ct)) continue;

            if (evt.BlockedBy(sub) is { Count: > 0 } blocked) {
                string reason = string.Join(",", blocked);
                logger.LogInformation("feed sub {Id}: {Summary} suppressed by {Reason}", sub.Id, evt.Summary, reason);
                await store.SuppressAsync(sub.Id, evt.EventKind, evt.DedupKey, reason, evt.Summary, ct);
                continue;
            }

            int? code = null;
            bool ok = false;
            try {
                string body = evt.BuildBody(sub.MessageTemplate);
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
                EventKind = evt.EventKind,
                DedupKey = evt.DedupKey,
                Summary = evt.Summary,
                Status = ok ? "sent" : "failed",
                AttemptedAt = DateTimeOffset.UtcNow,
                ResponseCode = code,
                Attempts = 1
            }, ct);

            if (ok) {
                await store.MarkDeliveredAsync(sub.Id, DateTimeOffset.UtcNow, ct);
            } else {
                await store.BumpFailAsync(sub.Id, ct);
                var refreshed = (await store.ActiveAsync(ct)).FirstOrDefault(s => s.Id == sub.Id);
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
