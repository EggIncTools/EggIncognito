using System.Text;
using EggIdentity.Client;
using EggIncognito.Data.Services;
using EggIncognito.Services.Admin;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Feed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/admin/feed")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("write")]
public sealed class AdminFeedController(IHttpClientFactory httpFactory) : ApiControllerBase {
    [HttpGet("subscriptions")]
    [EnableRateLimiting("read")]
    [RequiresDb]
    public async Task<IActionResult> Subscriptions([FromServices] FeedSubscriptionStore store,
        [FromServices] IdentityApiClient? identity, CancellationToken ct) {
        var subs = await store.AllForAdminAsync(ct);
        var usernames = new Dictionary<Guid, string>();
        if (identity is not null)
            foreach (var u in await identity.ListAdminUsersAsync(ct)) usernames[u.UserId] = u.Username;
        return Ok(FeedAdminGrouping.Build(subs, usernames));
    }

    [HttpPost("subscriptions/{id:int}/deactivate")]
    [RequiresDb]
    public async Task<IActionResult> Deactivate(int id, [FromServices] FeedSubscriptionStore store,
        [FromServices] AdminNotifier notifier, CancellationToken ct) {
        if (!await store.AdminDeactivateAsync(id, ct)) return Fail(404, "subscription not found");
        FeedSubscriptionNotify.Changed(notifier);
        return Ok(new { deactivated = true });
    }

    [HttpDelete("subscriptions/{id:int}")]
    [RequiresDb]
    public async Task<IActionResult> Delete(int id, [FromServices] FeedSubscriptionStore store,
        [FromServices] AdminNotifier notifier, CancellationToken ct) {
        if (!await store.AdminDeleteAsync(id, ct)) return Fail(404, "subscription not found");
        FeedSubscriptionNotify.Changed(notifier);
        return Ok(new { deleted = true });
    }

    [HttpPost("subscriptions/{id:int}/test")]
    [RequiresDb]
    public async Task<IActionResult> Test(int id, [FromQuery] string? sample,
        [FromServices] FeedSubscriptionStore store, CancellationToken ct) {
        var sub = await store.AdminByIdAsync(id, ct);
        if (sub is null) return Fail(404, "subscription not found");

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
            return Fail(400, "webhook rejected the test message");
        return Ok(new { tested = true, sample = chosen?.Key });
    }
}
