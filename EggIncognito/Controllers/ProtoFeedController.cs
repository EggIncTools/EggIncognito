using System.Text.Json;
using EggIdentity.Auth;
using EggIdentity.Client;
using EggIdentity.Contract;
using EggIdentity.Filters;
using EggIncognito.Services;
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
public sealed class ProtoFeedController(ICurrentUser currentUser, FeedSender sender)
    : ApiControllerBase {
    [HttpGet("kinds")]
    public async Task<IActionResult> Kinds([FromServices] FeedSampleSource samples, CancellationToken ct) {
        var views = new List<object>();
        foreach (var k in FeedEventKinds.All)
            views.Add(KindView(k, await samples.FieldsAsync(k, ct), await samples.ForAsync(k, ct)));
        return Ok(views);
    }

    public static object KindView(NotificationKind k, IReadOnlyList<FilterFieldDef> fields, IReadOnlyList<FeedSample> samples) => new {
        k.Key,
        k.Label,
        k.Description,
        Catalog = JsonElement.Parse(FilterCodec.SerializeCatalog(new FilterCatalog(k.Key, fields, k.Defaults))),
        Vars = FeedVars.Describe(k, samples)
    };

    public static FilterSet ParseFilter(JsonElement? raw, FilterSet fallback) =>
        raw is { ValueKind: JsonValueKind.Object } json ? FilterCodec.Deserialize(json.GetRawText()) : fallback;

    public static string? Problems(NotificationKind kind, FilterSet set) =>
        kind.Validate(set) is { Count: > 0 } problems
            ? string.Join("; ", problems.Select(p => $"row {p.Row + 1}: {kind.FieldLabel(p.Condition.Field)}: {p.Reason}"))
            : null;

    public static async Task<string> TestBodyAsync(
        FeedSubscription sub, string? sample, FeedSampleSource samples, CancellationToken ct) {
        var kind = FeedEventKinds.Find(FeedEventKinds.Normalize(sub.EventKind)) ?? FeedEventKinds.Proto;
        var chosen = await samples.FindAsync(kind, sample, ct);
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
        bool dm = string.Equals(req.Channel, "dm", StringComparison.OrdinalIgnoreCase);
        string? discordId = currentUser.Current.DiscordId;
        Uri? webhook = null;
        if (dm && string.IsNullOrEmpty(discordId))
            return Fail(400, "link a Discord account to get DMs");
        if (!dm && (string.IsNullOrWhiteSpace(req.WebhookUrl) ||
            !Uri.TryCreate(req.WebhookUrl, UriKind.Absolute, out webhook) ||
            webhook.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(webhook.Host, "discord.com", StringComparison.OrdinalIgnoreCase) ||
            !webhook.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal)))
            return Fail(400, "a Discord webhook URL is required");

        var info = FeedEventKinds.Find(FeedEventKinds.Normalize(req.EventKind)) ?? FeedEventKinds.Proto;
        FilterSet filter;
        try {
            filter = ParseFilter(req.Filter, info.Defaults);
        } catch (JsonException) {
            return Fail(400, "the filter is not valid JSON");
        }

        if (Problems(info, filter) is { } problems) return Fail(400, problems);

        var target = new FeedSubscription {
            Kind = dm ? FeedSender.DmKind : FeedSender.WebhookKind,
            TargetUrl = dm ? discordId! : webhook!.ToString()
        };
        var test = await sender.SendAsync(target, ConnectedBody, ct);
        if (!test.Ok)
            return Fail(400, dm ? "Discord refused the test DM" : "webhook rejected the test message");

        var sub = await store.AddAsync(new FeedSubscription {
            Kind = target.Kind,
            EventKind = info.Key,
            TargetUrl = target.TargetUrl,
            FilterSet = FilterCodec.Serialize(filter),
            Label = string.IsNullOrWhiteSpace(req.Label) ? null : req.Label.Trim(),
            MessageTemplate = string.IsNullOrWhiteSpace(req.MessageTemplate) ? null : req.MessageTemplate,
            OwnerUserId = owner.Value
        }, ct);
        FeedSubscriptionNotify.Changed(notifier);
        return Ok(new { sub.Id, sub.EventKind });
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
            Filter = FilterJson(s),
            s.Active,
            s.CreatedAt,
            s.LastDeliveryAt,
            s.FailCount,
            s.MessageTemplate,
            UrlMasked = FeedSender.Describe(s)
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
        [FromServices] FeedSubscriptionStore store, [FromServices] FeedSampleSource samples, CancellationToken ct) {
        var owner = currentUser.Current.Id;
        if (owner is null) return Fail(401, "log in to manage subscriptions");

        var sub = (await store.ByOwnerAsync(owner.Value, ct)).FirstOrDefault(s => s.Id == id);
        if (sub is null) return Fail(404, "subscription not found");

        var res = await sender.SendAsync(sub, await TestBodyAsync(sub, sample, samples, ct), ct);
        if (!res.Ok)
            return Fail(400, FeedSender.IsDm(sub) ? "Discord refused the test DM" : "webhook rejected the test message");
        return Ok(new { tested = true, sample });
    }

    [HttpGet("dm/reachability")]
    public async Task<IActionResult> DmReachability([FromServices] AuthState auth,
        [FromServices] IdentityApiClient? identity, CancellationToken ct) {
        if (currentUser.Current.Id is null) return Fail(401, "log in to manage subscriptions");
        string token = Request.Cookies[auth.SessionCookieName] ?? "";
        var res = identity is null || token.Length == 0 ? null : await identity.GetDmReachabilityAsync(token, AppName, ct);
        return Ok(res ?? new DmReachabilityResponse {
            State = string.IsNullOrEmpty(currentUser.Current.DiscordId) ? EggIdentity.Contract.DmReachability.NoDiscordLink : EggIdentity.Contract.DmReachability.Unknown
        });
    }

    [HttpPost("dm/test")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> DmTest(CancellationToken ct) {
        if (currentUser.Current.Id is null) return Fail(401, "log in to manage subscriptions");
        if (currentUser.Current.DiscordId is not { Length: > 0 } discordId)
            return Ok(new DmSendResult(DmSendStatus.Failed));
        return Ok(await sender.DmAsync(discordId, DmTestBody, ct));
    }

    private const string AppName = "eggincognito";
    private const string ConnectedBody = """{"content":"EggIncognito notifications connected."}""";
    private const string DmTestBody = """{"content":"EggIncognito test DM. Notifications can reach you here."}""";

    [HttpPost("preview")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Preview([FromBody] FeedPreviewReq req, [FromServices] FeedSampleSource samples,
        CancellationToken ct) {
        var info = FeedEventKinds.Find(FeedEventKinds.Normalize(req.EventKind)) ?? FeedEventKinds.Proto;
        FilterSet filter;
        try {
            filter = ParseFilter(req.Filter, info.Defaults);
        } catch (JsonException) {
            return Fail(400, "the filter is not valid JSON");
        }

        return Ok(PreviewRows(info, await samples.ForAsync(info, ct), filter,
            string.IsNullOrWhiteSpace(req.MessageTemplate) ? null : req.MessageTemplate));
    }

    public static List<FeedPreviewRow> PreviewRows(
        NotificationKind info, IReadOnlyList<FeedSample> samples, FilterSet filter, string? template) =>
        [.. samples.Select(s => {
            var result = info.Evaluate(s.Event, filter);
            return new FeedPreviewRow(
                s.Key, s.Label, s.Event.Summary, result.Matched,
                [.. result.Failed.Select(f => $"{info.FieldLabel(f.Condition.Field)} {f.Condition.Op} {f.Condition.Value}".Trim())],
                DiscordFeedPayload.Build(s.Event, template));
        })];

    public static JsonElement FilterJson(FeedSubscription sub) {
        var info = FeedEventKinds.Find(FeedEventKinds.Normalize(sub.EventKind)) ?? FeedEventKinds.Proto;
        return JsonElement.Parse(FilterCodec.Serialize(info.FilterOf(sub)));
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

        var info = FeedEventKinds.Find(FeedEventKinds.Normalize(sub.EventKind)) ?? FeedEventKinds.Proto;
        FilterSet filter;
        try {
            filter = ParseFilter(req.Filter, info.FilterOf(sub));
        } catch (JsonException) {
            return Fail(400, "the filter is not valid JSON");
        }

        if (Problems(info, filter) is { } problems) return Fail(400, problems);

        bool ok = await store.UpdateAsync(
            id, owner.Value,
            req.Active ?? sub.Active,
            req.MessageTemplate,
            FilterCodec.Serialize(filter),
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

    public static string MaskWebhook(string url) => WebhookMask.Mask(url);
}
