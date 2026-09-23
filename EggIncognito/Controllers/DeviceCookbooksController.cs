using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/devices")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("read")]
public sealed class DeviceCookbooksController(
    ICurrentUser currentUser,
    CookbookCancellations cancellations) : ApiControllerBase {
    [HttpGet("{id}/cookbooks")]
    [RequiresDb]
    public async Task<IActionResult> List(string id, [FromServices] DeviceCookbookRunner runner,
        CancellationToken ct) {
        if (await runner.TargetAsync(id, ct) is null) return Fail(404, "unknown device");
        IReadOnlyList<DeviceCookbookInfo> infos = await runner.DescribeAsync(id, ct);
        return Ok(infos);
    }

    [HttpPost("{id}/cookbooks")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Start(string id, [FromBody] DeviceCookbookRequest? request,
        [FromServices] DeviceCookbookRunner runner, CancellationToken ct) {
        if (request is null || string.IsNullOrWhiteSpace(request.CookbookId))
            return Fail(400, "cookbookId required");

        string who = currentUser.DiscordId ?? "?";
        var start = await runner.StartAsync(id, request, $"admin:{who}", ct);
        return start.Outcome switch {
            DeviceCookbookStartOutcome.Started =>
                Accepted(new { device = id, jobId = start.JobId, cookbook = request.CookbookId, state = "running" }),
            DeviceCookbookStartOutcome.UnknownDevice => Fail(404, start.Error!),
            DeviceCookbookStartOutcome.UnknownCookbook => Fail(404, start.Error!),
            DeviceCookbookStartOutcome.Unavailable => Fail(409, start.Error!),
            DeviceCookbookStartOutcome.Busy => Fail(409, start.Error!),
            _ => Fail(503, start.Error ?? "cookbooks are not configured")
        };
    }

    [HttpGet("{id}/cookbooks/running")]
    [RequiresDb]
    public async Task<IActionResult> Running(string id, [FromServices] DeviceCookbookRunner runner,
        [FromServices] DeviceTimelineCache timeline, CancellationToken ct) {
        if (await runner.TargetAsync(id, ct) is null) return Fail(404, "unknown device");

        var latest = await timeline.LatestAsync(id, DeviceJobKinds.Cookbook, ct);
        bool running = latest is { State: DeviceJobStates.Running };
        return Ok(new { running, jobId = running ? latest!.Id : (long?)null });
    }

    [HttpPost("{id}/cookbooks/stop")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Stop(string id, [FromServices] DeviceCookbookRunner runner,
        [FromServices] DeviceJobStore jobs, CancellationToken ct) {
        if (await runner.TargetAsync(id, ct) is null) return Fail(404, "unknown device");

        if (cancellations.TryCancel(id, out long jobId)) return Ok(new { ok = true, jobId, live = true });

        long? orphan = await jobs.CancelRunningAsync(id, DeviceJobKinds.Cookbook,
            "stopped from the console; no live worker held this job", ct);
        return orphan is { } orphanId
            ? Ok(new { ok = true, jobId = orphanId, live = false })
            : Fail(409, "no cookbook is running on this device");
    }

    [HttpGet("{id}/cookbooks/run/{jobId:long}")]
    [RequiresDb]
    public async Task<IActionResult> Run(string id, long jobId, [FromServices] DeviceCookbookFeed feed,
        CancellationToken ct) {
        if (await feed.RunAsync(id, jobId, ct) is not { } status)
            return Fail(404, "unknown cookbook run for this device");
        return Ok(status);
    }
}
