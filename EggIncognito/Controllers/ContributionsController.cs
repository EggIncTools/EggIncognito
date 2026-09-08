using EggIncognito.Capture;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Contributions;
using EggIncognito.Services;
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
    ContributionOptions options,
    IServiceProvider services) : ControllerBase {
    private const int MaxPageSize = 200;

    private ContributionStore? Store => services.GetService(typeof(ContributionStore)) as ContributionStore;

    private (Guid UserId, IActionResult? Error) Me() =>
        currentUser.IsAuthenticated && currentUser.UserId is { } id
            ? (id, null)
            : (Guid.Empty, StatusCode(401, new { error = "log in to use contributions" }));

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct) {
        (var userId, var error) = Me();
        if (error is not null) return error;
        if (Store is not { } store) return StatusCode(503, new { error = "no database configured" });

        var counts = await store.CountsForAsync(userId, ct);
        return Ok(new ContributionSummaryDto(
            options.Enabled,
            counts.Recorded, counts.Submitted, counts.Approved, counts.Rejected,
            options.MaxRecordedPerUser,
            kinds.KindNames,
            [.. kinds.AllRoutes.OrderBy(r => r, StringComparer.Ordinal)]));
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(
        [FromQuery] string? status, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct) {
        (var userId, var error) = Me();
        if (error is not null) return error;
        if (Store is not { } store) return StatusCode(503, new { error = "no database configured" });
        if (status is not null && !ContributedCaptureStatus.IsKnown(status))
            return BadRequest(new { error = $"unknown status {status}" });

        var page = await store.MineAsync(userId, status, Math.Max(skip, 0), Clamp(take), ct);
        return Ok(new {
            total = page.Total,
            rows = page.Rows.Select(r => new ContributionRowDto(
                r.Id, r.Kind, r.Status, r.Summary, r.ClientVersion, r.RecordedAt, r.SubmittedAt))
        });
    }

    [HttpPost("submit")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Submit(CancellationToken ct) {
        (var userId, var error) = Me();
        if (error is not null) return error;
        if (!options.Enabled) return StatusCode(403, new { error = "contributions are disabled" });
        if (Store is not { } store) return StatusCode(503, new { error = "no database configured" });

        var counts = await store.CountsForAsync(userId, ct);
        if (counts.Submitted >= options.MaxSubmittedPerUser)
            return StatusCode(429, new { error = "you have too many submissions awaiting review" });

        int sent = await store.SubmitAsync(userId, ct);
        return Ok(new { submitted = sent });
    }

    [HttpPost("discard")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Discard(CancellationToken ct) {
        (var userId, var error) = Me();
        if (error is not null) return error;
        if (Store is not { } store) return StatusCode(503, new { error = "no database configured" });
        int dropped = await store.DiscardAsync(userId, ct);
        return Ok(new { discarded = dropped });
    }

    [HttpPost("offer")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public IActionResult Offer([FromBody] ContributionOfferRequest body) {
        (var userId, var error) = Me();
        if (error is not null) return error;
        if (!options.Enabled) return StatusCode(403, new { error = "contributions are disabled" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });
        if (services.GetService(typeof(ContributionRecorder)) is not ContributionRecorder recorder)
            return StatusCode(503, new { error = "contribution recording is off" });
        if (services.GetService(typeof(DeviceCaptureManager)) is not DeviceCaptureManager captures)
            return StatusCode(503, new { error = "device capture is not configured" });
        if (string.IsNullOrWhiteSpace(body.DeviceId)) return BadRequest(new { error = "deviceId required" });

        var flow = captures.HubFor(body.DeviceId)?.Snapshot().FirstOrDefault(f => f.Id == body.FlowId);
        if (flow is null) return NotFound(new { error = "flow not found on this device's capture" });
        if (kinds.For(flow.Path) is null)
            return BadRequest(new { error = $"{flow.Path} is not a contributable route" });

        recorder.Record(userId, flow);
        return Accepted(new { recorded = true, path = flow.Path });
    }

    [HttpGet("pending")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Pending(
        [FromQuery] string? kind, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct) {
        if (Store is not { } store) return StatusCode(503, new { error = "no database configured" });
        var page = await store.PendingAsync(kind, Math.Max(skip, 0), Clamp(take), ct);
        return Ok(new {
            total = page.Total,
            rows = page.Rows.Select(r => new ContributionPendingRowDto(
                r.Id, r.ContributorUserId, r.Kind, r.Summary, r.Payload, r.ClientVersion,
                r.RecordedAt, r.SubmittedAt))
        });
    }

    [HttpGet("tallies")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Tallies(CancellationToken ct) {
        if (Store is not { } store) return StatusCode(503, new { error = "no database configured" });
        var counts = await store.CountsAllAsync(ct);
        var tallies = await store.PendingTalliesAsync(50, ct);
        return Ok(new { counts, tallies });
    }

    [HttpPost("review")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Review([FromBody] ContributionReviewRequest body, CancellationToken ct) {
        if (Store is not { } store) return StatusCode(503, new { error = "no database configured" });
        if (body.Ids.Count == 0) return BadRequest(new { error = "no ids supplied" });
        if (body.Ids.Count > 5000) return BadRequest(new { error = "too many ids in one review" });

        int changed = await store.ReviewAsync(body.Ids, body.Approve, Reviewer(), body.Note, ct);
        return Ok(new { reviewed = changed, approved = body.Approve });
    }

    [HttpPost("review-contributor")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> ReviewContributor(
        [FromBody] ContributionContributorReviewRequest body, CancellationToken ct) {
        if (Store is not { } store) return StatusCode(503, new { error = "no database configured" });
        if (body.ContributorUserId == Guid.Empty) return BadRequest(new { error = "contributorUserId required" });
        if (string.IsNullOrWhiteSpace(body.Kind)) return BadRequest(new { error = "kind required" });

        int changed = await store.ReviewContributorAsync(
            body.ContributorUserId, body.Kind, body.Approve, Reviewer(), body.Note, ct);
        return Ok(new { reviewed = changed, approved = body.Approve });
    }

    private string Reviewer() => currentUser.Username ?? currentUser.UserId?.ToString() ?? "admin";

    private static int Clamp(int take) => take <= 0 ? 50 : Math.Min(take, MaxPageSize);
}
