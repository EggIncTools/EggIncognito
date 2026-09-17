using System.Text;
using EggIdentity.Contract;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Protos;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Feed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/protos/feed")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class ProtoFeedController(IServiceProvider services, IHttpClientFactory httpFactory)
    : ControllerBase {
    private FeedSubscriptionStore? Store =>
        services.GetService(typeof(FeedSubscriptionStore)) as FeedSubscriptionStore;

    private ICurrentUser? CurrentUser => services.GetService(typeof(ICurrentUser)) as ICurrentUser;

    private Guid? OwnerUserId => CurrentUser?.UserId;

    [HttpGet("kinds")]
    public IActionResult Kinds() => Ok(FeedEventKinds.All.Select(k => new {
        k.Key,
        k.Label,
        k.Triggers,
        k.DefaultTrigger,
        k.PlatformScoped,
        k.Filters,
        Vars = FeedVars.Describe(k)
    }));

    [HttpPost]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Create([FromBody] FeedCreateReq req, CancellationToken ct) {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });
        if (string.IsNullOrWhiteSpace(req.WebhookUrl) ||
            !Uri.TryCreate(req.WebhookUrl, UriKind.Absolute, out var webhook) ||
            webhook.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(webhook.Host, "discord.com", StringComparison.OrdinalIgnoreCase) ||
            !webhook.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal))
            return BadRequest(new { error = "a Discord webhook URL is required" });

        var http = httpFactory.CreateClient("discord-api");
        var test = await http.PostAsync(webhook,
            new StringContent("""{"content":"EggIncognito proto feed connected."}""",
                Encoding.UTF8, "application/json"), ct);
        if (!test.IsSuccessStatusCode)
            return BadRequest(new { error = "webhook rejected the test message" });

        string kind = FeedEventKinds.Normalize(req.EventKind);
        var sub = await Store.AddAsync(new FeedSubscription {
            Kind = "discord",
            EventKind = kind,
            TargetUrl = webhook.ToString(),
            Platforms = req.Platforms is { Length: > 0 } ? req.Platforms : ["android", "ios"],
            Trigger = FeedEventKinds.NormalizeTrigger(kind, req.Trigger),
            Filters = FeedEventKinds.NormalizeFilters(kind, req.Filters),
            Label = req.Label,
            MessageTemplate = string.IsNullOrWhiteSpace(req.MessageTemplate) ? null : req.MessageTemplate,
            OwnerUserId = owner.Value
        }, ct);
        FeedSubscriptionNotify.Changed(services);
        return Ok(new { sub.Id, sub.EventKind, sub.Platforms, sub.Trigger });
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct) {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        var subs = await Store.ByOwnerAsync(owner.Value, ct);
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
    public async Task<IActionResult> Delete(int id, CancellationToken ct) {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        bool ok = await Store.DeleteAsync(id, owner.Value, ct);
        if (!ok) return NotFound(new { error = "subscription not found" });
        FeedSubscriptionNotify.Changed(services);
        return Ok(new { deleted = true });
    }

    [HttpPost("{id:int}/test")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Test(int id, [FromQuery] string? sample, CancellationToken ct) {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        var sub = (await Store.ByOwnerAsync(owner.Value, ct)).FirstOrDefault(s => s.Id == id);
        if (sub is null) return NotFound(new { error = "subscription not found" });

        string kind = FeedEventKinds.Normalize(sub.EventKind);
        var fallback = FeedSamples.For(kind);
        var chosen = FeedSamples.Find(kind, sample) ?? (fallback.Count > 0 ? fallback[0] : null);
        string body = chosen is null
            ? """{"content":"EggIncognito feed test."}"""
            : DiscordFeedPayload.MarkAsTest(chosen.Event.BuildBody(sub.MessageTemplate));

        var http = httpFactory.CreateClient("discord-api");
        var res = await http.PostAsync(sub.TargetUrl,
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        if (!res.IsSuccessStatusCode)
            return BadRequest(new { error = "webhook rejected the test message" });
        return Ok(new { tested = true, sample = chosen?.Key });
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

        return Ok(FeedSamples.For(kind).Select(s => {
            bool matches = s.Event.Matches(probe);
            var blocked = matches ? s.Event.BlockedBy(probe) : [];
            return new FeedPreviewRow(
                s.Key, s.Label, s.Event.Summary, matches, blocked,
                matches && blocked.Count == 0 ? s.Event.BuildBody(probe.MessageTemplate) : null);
        }));
    }

    [HttpPatch("{id:int}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Update(int id, [FromBody] FeedUpdateReq req, CancellationToken ct) {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        var sub = (await Store.ByOwnerAsync(owner.Value, ct)).FirstOrDefault(s => s.Id == id);
        if (sub is null) return NotFound(new { error = "subscription not found" });

        string trigger = FeedEventKinds.NormalizeTrigger(
            FeedEventKinds.Normalize(sub.EventKind), req.Trigger ?? sub.Trigger);
        bool ok = await Store.UpdateAsync(
            id, owner.Value,
            req.Platforms ?? ["android", "ios"],
            trigger,
            req.Active ?? true,
            req.MessageTemplate,
            ResolveFilters(sub, req.Filters), ct);
        if (!ok) return NotFound(new { error = "subscription not found" });
        FeedSubscriptionNotify.Changed(services);
        return Ok(new { updated = true });
    }

    [HttpGet("{id:int}/activity")]
    public async Task<IActionResult> Activity(int id, CancellationToken ct) {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is not { } store) return StatusCode(503, new { error = "no database configured" });

        var sub = (await store.ByOwnerAsync(owner.Value, ct)).FirstOrDefault(s => s.Id == id);
        if (sub is null) return NotFound(new { error = "subscription not found" });
        return Ok(await ActivityRowsAsync(store, id, ct));
    }

    [HttpGet("admin/{id:int}/activity")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> AdminActivity(int id, CancellationToken ct) {
        if (CurrentUser is not { } user || !user.IsAtLeast(UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        if (Store is not { } store) return StatusCode(503, new { error = "no database configured" });
        if (await store.AdminByIdAsync(id, ct) is null) return NotFound(new { error = "subscription not found" });
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
