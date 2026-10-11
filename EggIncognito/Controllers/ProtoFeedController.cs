using System.Text;
using EggIdentity.Auth;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Protos;
using EggIncognito.Services.Admin;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Feed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/protos/feed")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class ProtoFeedController(ICurrentUser currentUser, IHttpClientFactory httpFactory)
    : ApiControllerBase {
    [HttpGet("kinds")]
    public IActionResult Kinds() => Ok(FeedEventKinds.All.Select(KindView));

    public static object KindView(NotificationKind k) => new {
        k.Key,
        k.Label,
        k.Description,
        Triggers = k.Triggers.Select(t => new { t.Value, t.Label }),
        k.DefaultTrigger,
        k.PlatformScoped,
        Filters = k.Filters.Select(f => new { f.Key, f.Label, f.DefaultOn }),
        Vars = FeedVars.Describe(k)
    };

    public static string TestBody(FeedSubscription sub, string? sample) {
        string kind = FeedEventKinds.Normalize(sub.EventKind);
        var fallback = FeedEventKinds.Samples(kind);
        var chosen = FeedEventKinds.Sample(kind, sample) ?? (fallback.Count > 0 ? fallback[0] : null);
        return chosen is null
            ? """{"content":"EggIncognito feed test."}"""
            : DiscordFeedPayload.MarkAsTest(DiscordFeedPayload.Build(chosen.Event, sub.MessageTemplate));
    }

    [HttpPost]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Create([FromBody] FeedCreateReq req,
        [FromServices] FeedSubscriptionStore store, [FromServices] AdminNotifier? notifier, CancellationToken ct) {
        var owner = currentUser.Current.Id;
        if (owner is null) return Fail(401, "log in to manage subscriptions");
        if (string.IsNullOrWhiteSpace(req.WebhookUrl) ||
            !Uri.TryCreate(req.WebhookUrl, UriKind.Absolute, out var webhook) ||
            webhook.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(webhook.Host, "discord.com", StringComparison.OrdinalIgnoreCase) ||
            !webhook.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal))
            return Fail(400, "a Discord webhook URL is required");

        var http = httpFactory.CreateClient("discord-api");
        var test = await http.PostAsync(webhook,
            new StringContent("""{"content":"EggIncognito proto feed connected."}""",
                Encoding.UTF8, "application/json"), ct);
        if (!test.IsSuccessStatusCode)
            return Fail(400, "webhook rejected the test message");

        string kind = FeedEventKinds.Normalize(req.EventKind);
        var sub = await store.AddAsync(new FeedSubscription {
            Kind = "discord",
            EventKind = kind,
            TargetUrl = webhook.ToString(),
            Platforms = req.Platforms is { Length: > 0 } ? req.Platforms : ["android", "ios"],
            Trigger = FeedEventKinds.NormalizeTrigger(kind, req.Trigger),
            Filters = FeedEventKinds.NormalizeFilters(kind, req.Filters),
            Label = string.IsNullOrWhiteSpace(req.Label) ? null : req.Label.Trim(),
            MessageTemplate = string.IsNullOrWhiteSpace(req.MessageTemplate) ? null : req.MessageTemplate,
            OwnerUserId = owner.Value
        }, ct);
        FeedSubscriptionNotify.Changed(notifier);
        return Ok(new { sub.Id, sub.EventKind, sub.Platforms, sub.Trigger });
    }

    [HttpGet("mine")]
    [RequiresDb]
    public async Task<IActionResult> Mine([FromServices] FeedSubscriptionStore store, CancellationToken ct) {
        var owner = currentUser.Current.Id;
        if (owner is null) return Fail(401, "log in to manage subscriptions");

        var subs = await store.ByOwnerAsync(owner.Value, ct);
        return Ok(subs.Select(s => new {
            s.Id,
            s.Label,
            EventKind = FeedEventKinds.Normalize(s.EventKind),
            s.Platforms,
            s.Trigger,
            s.Filters,
            s.Active,
            s.CreatedAt,
            s.LastDeliveryAt,
            s.FailCount,
            s.MessageTemplate,
            UrlMasked = MaskWebhook(s.TargetUrl)
        }));
    }

    [HttpDelete("{id:int}")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Delete(int id, [FromServices] FeedSubscriptionStore store,
        [FromServices] AdminNotifier? notifier, CancellationToken ct) {
        var owner = currentUser.Current.Id;
        if (owner is null) return Fail(401, "log in to manage subscriptions");

        bool ok = await store.DeleteAsync(id, owner.Value, ct);
        if (!ok) return Fail(404, "subscription not found");
        FeedSubscriptionNotify.Changed(notifier);
        return Ok(new { deleted = true });
    }

    [HttpPost("{id:int}/test")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Test(int id, [FromQuery] string? sample,
        [FromServices] FeedSubscriptionStore store, CancellationToken ct) {
        var owner = currentUser.Current.Id;
        if (owner is null) return Fail(401, "log in to manage subscriptions");

        var sub = (await store.ByOwnerAsync(owner.Value, ct)).FirstOrDefault(s => s.Id == id);
        if (sub is null) return Fail(404, "subscription not found");

        var http = httpFactory.CreateClient("discord-api");
        var res = await http.PostAsync(sub.TargetUrl,
            new StringContent(TestBody(sub, sample), Encoding.UTF8, "application/json"), ct);
        if (!res.IsSuccessStatusCode)
            return Fail(400, "webhook rejected the test message");
        return Ok(new { tested = true, sample });
    }

    [HttpPost("preview")]
    [EnableRateLimiting("read")]
    public IActionResult Preview([FromBody] FeedPreviewReq req) {
        string kind = FeedEventKinds.Normalize(req.EventKind);
        var probe = new FeedSubscription {
            EventKind = kind,
            Platforms = req.Platforms is { Length: > 0 } ? req.Platforms : ["android", "ios"],
            Trigger = FeedEventKinds.NormalizeTrigger(kind, req.Trigger),
            Filters = FeedEventKinds.NormalizeFilters(kind, req.Filters),
            MessageTemplate = string.IsNullOrWhiteSpace(req.MessageTemplate) ? null : req.MessageTemplate
        };

        return Ok(PreviewRows(probe));
    }

    public static List<FeedPreviewRow> PreviewRows(FeedSubscription probe) {
        var info = FeedEventKinds.Find(FeedEventKinds.Normalize(probe.EventKind)) ?? FeedEventKinds.Proto;
        return [.. info.Samples.Select(s => {
            bool matches = info.Matches(s.Event, probe);
            var blocked = matches ? info.BlockedBy(s.Event, probe) : [];
            return new FeedPreviewRow(
                s.Key, s.Label, s.Event.Summary, matches, blocked,
                DiscordFeedPayload.Build(s.Event, probe.MessageTemplate));
        })];
    }

    [HttpPatch("{id:int}")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Update(int id, [FromBody] FeedUpdateReq req,
        [FromServices] FeedSubscriptionStore store, [FromServices] AdminNotifier? notifier, CancellationToken ct) {
        var owner = currentUser.Current.Id;
        if (owner is null) return Fail(401, "log in to manage subscriptions");

        var sub = (await store.ByOwnerAsync(owner.Value, ct)).FirstOrDefault(s => s.Id == id);
        if (sub is null) return Fail(404, "subscription not found");

        string trigger = FeedEventKinds.NormalizeTrigger(
            FeedEventKinds.Normalize(sub.EventKind), req.Trigger ?? sub.Trigger);
        bool ok = await store.UpdateAsync(
            id, owner.Value,
            req.Platforms ?? ["android", "ios"],
            trigger,
            req.Active ?? true,
            req.MessageTemplate,
            ResolveFilters(sub, req.Filters),
            req.Label ?? sub.Label, ct);
        if (!ok) return Fail(404, "subscription not found");
        FeedSubscriptionNotify.Changed(notifier);
        return Ok(new { updated = true });
    }

    [HttpGet("{id:int}/activity")]
    [RequiresDb]
    public async Task<IActionResult> Activity(int id, [FromServices] FeedSubscriptionStore store,
        CancellationToken ct) {
        var owner = currentUser.Current.Id;
        if (owner is null) return Fail(401, "log in to manage subscriptions");

        var sub = (await store.ByOwnerAsync(owner.Value, ct)).FirstOrDefault(s => s.Id == id);
        if (sub is null) return Fail(404, "subscription not found");
        return Ok(await ActivityRowsAsync(store, id, ct));
    }

    [HttpGet("admin/{id:int}/activity")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    public async Task<IActionResult> AdminActivity(int id, [FromServices] FeedSubscriptionStore store,
        CancellationToken ct) {
        if (await store.AdminByIdAsync(id, ct) is null) return Fail(404, "subscription not found");
        return Ok(await ActivityRowsAsync(store, id, ct));
    }

    private static async Task<List<FeedActivityRow>> ActivityRowsAsync(
        FeedSubscriptionStore store, int id, CancellationToken ct) {
        var deliveries = await store.DeliveriesAsync(id, ActivityTake, ct);
        var suppressions = await store.SuppressionsAsync(id, ActivityTake, ct);

        return [.. deliveries
            .Select(d => new FeedActivityRow(d.AttemptedAt, d.Status,
                string.IsNullOrEmpty(d.Summary) ? d.DedupKey : d.Summary, d.ResponseCode, null))
            .Concat(suppressions.Select(s => new FeedActivityRow(s.CreatedAt, "blocked",
                string.IsNullOrEmpty(s.Summary) ? s.DedupKey : s.Summary, null, s.Reason)))
            .OrderByDescending(r => r.At)
            .Take(ActivityTake)];
    }

    private const int ActivityTake = 25;

    public static string[] ResolveFilters(FeedSubscription sub, string[]? requested) =>
        FeedEventKinds.NormalizeFilters(FeedEventKinds.Normalize(sub.EventKind), requested ?? sub.Filters);

    public static string MaskWebhook(string url) => WebhookMask.Mask(url);
}
