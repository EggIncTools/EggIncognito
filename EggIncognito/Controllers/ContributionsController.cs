using EggIdentity.Auth;
using EggIncognito.Capture;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Contributions;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Contributions;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/contributions")]
[ApiAccess(ApiAccessLevel.Authenticated)]
public sealed class ContributionsController(
    ICurrentUser currentUser,
    ICaptureContributionKinds kinds,
    ContributionOptions options) : ApiControllerBase {
    private const int MaxPageSize = 200;
    private const int MaxOfferBatch = 5000;

    private static int Clamp(int take) => take <= 0 ? 50 : Math.Min(take, MaxPageSize);

    private (Guid UserId, IActionResult? Error) Me() =>
        currentUser.Current is { IsAuthenticated: true, Id: { } id }
            ? (id, null)
            : (Guid.Empty, Fail(401, "log in to use contributions"));

    [HttpGet("summary")]
    [RequiresDb]
    public async Task<IActionResult> Summary([FromServices] ContributionStore store, CancellationToken ct) {
        (var userId, var error) = Me();
        if (error is not null) return error;

        var counts = await store.CountsForAsync(userId, ct);
        return Ok(new ContributionSummaryDto(
            options.Enabled,
            counts.Recorded, counts.Submitted, counts.Approved, counts.Rejected,
            options.MaxRecordedPerUser,
            kinds.KindNames,
            [.. kinds.AllRoutes.OrderBy(r => r, StringComparer.Ordinal)]));
    }

    [HttpGet("mine")]
    [RequiresDb]
    public async Task<IActionResult> Mine(
        [FromServices] ContributionStore store,
        [FromQuery] string? status, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct) {
        (var userId, var error) = Me();
        if (error is not null) return error;
        if (status is not null && !ContributedCaptureStatus.IsKnown(status))
            return Fail(400, $"unknown status {status}");

        var page = await store.MineAsync(userId, status, Math.Max(skip, 0), Clamp(take), ct);
        return Ok(new {
            total = page.Total,
            rows = page.Rows.Select(r => new ContributionRowDto(
                r.Id, r.Kind, r.Status, r.Summary, r.ClientVersion, r.RecordedAt, r.SubmittedAt))
        });
    }

    [HttpPost("submit")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Submit([FromServices] ContributionStore store, CancellationToken ct) {
        (var userId, var error) = Me();
        if (error is not null) return error;
        if (!options.Enabled) return Fail(403, "contributions are disabled");

        var counts = await store.CountsForAsync(userId, ct);
        if (counts.Submitted >= options.MaxSubmittedPerUser)
            return Fail(429, "you have too many submissions awaiting review");

        int sent = await store.SubmitAsync(userId, ct);
        return Ok(new ContributionSubmitResult(sent));
    }

    [HttpPost("discard")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Discard([FromServices] ContributionStore store, CancellationToken ct) {
        (var userId, var error) = Me();
        if (error is not null) return error;
        int dropped = await store.DiscardAsync(userId, ct);
        return Ok(new { discarded = dropped });
    }

    [HttpPost("offer")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public IActionResult Offer([FromBody] ContributionOfferRequest body,
        [FromServices] ContributionRecorder recorder, [FromServices] IDeviceCaptureHubs captures) {
        (var userId, var error) = Me();
        if (error is not null) return error;
        if (OfferBlocked() is { } blocked) return blocked;
        if (string.IsNullOrWhiteSpace(body.DeviceId)) return Fail(400, "deviceId required");

        var flow = captures.HubFor(body.DeviceId)?.Snapshot().FirstOrDefault(f => f.Id == body.FlowId);
        if (flow is null) return Fail(404, "flow not found on this device's capture");
        if (kinds.For(flow.Path) is null)
            return Fail(400, $"{flow.Path} is not a contributable route");

        recorder.Record(userId, flow);
        return Accepted(new { recorded = true, path = flow.Path });
    }

    [HttpPost("offer-batch")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public IActionResult OfferBatch([FromBody] ContributionOfferBatchRequest body,
        [FromServices] ContributionRecorder recorder, [FromServices] IDeviceCaptureHubs captures) {
        (var userId, var error) = Me();
        if (error is not null) return error;
        if (OfferBlocked() is { } blocked) return blocked;
        if (string.IsNullOrWhiteSpace(body.DeviceId)) return Fail(400, "deviceId required");
        if (body.Ids is []) return Fail(400, "no flow ids supplied");
        if (body.Ids.Count > MaxOfferBatch) return Fail(400, "too many flow ids in one offer");

        var byId = new Dictionary<long, DashboardFlow>();
        foreach (var f in captures.HubFor(body.DeviceId)?.Snapshot() ?? []) byId[f.Id] = f;

        var missing = new List<long>();
        int recorded = 0;
        foreach (long id in body.Ids.Distinct()) {
            var flow = byId.GetValueOrDefault(id);
            if (flow is null || kinds.For(flow.Path) is null) {
                missing.Add(id);
                continue;
            }

            recorder.Record(userId, flow);
            recorded++;
        }

        return Accepted(new ContributionOfferBatchResult(recorded, missing));
    }

    private ObjectResult? OfferBlocked() =>
        options.Enabled ? null : Fail(403, "contributions are disabled");

}
