using EggIdentity.Contract;
using EggIncognito.Core.Services.Devices;
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
public sealed class DeviceIslandsController(
    ICurrentUser currentUser,
    IServiceProvider services) : ControllerBase {
    private DeviceCookbookRunner? Runner =>
        services.GetService(typeof(DeviceCookbookRunner)) as DeviceCookbookRunner;

    private DeviceIslandStore? Islands =>
        services.GetService(typeof(DeviceIslandStore)) as DeviceIslandStore;

    private ObjectResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin role required" });

    [HttpGet("{id}/islands")]
    public async Task<IActionResult> List(string id, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (Runner is not { } runner || Islands is not { } store)
            return StatusCode(503, new { error = "no database configured" });
        if (await runner.TargetAsync(id, ct) is null) return NotFound(new { error = "unknown device" });

        var islands = await store.ListAsync(id, ct);
        return Ok(islands.Select(Project));
    }

    [HttpPost("{id}/islands")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Create(string id, [FromBody] CreateIslandRequest? request, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (Runner is not { } runner || Islands is not { } store)
            return StatusCode(503, new { error = "no database configured" });
        if (await runner.TargetAsync(id, ct) is null) return NotFound(new { error = "unknown device" });

        string who = currentUser.DiscordId ?? "?";
        var run = await runner.RunNowAsync(id,
            new DeviceCookbookRequest(DeviceCookbookIds.CreateIsland, request?.Label), $"admin:{who}", ct);
        if (!run.Ok) return StatusCode(502, new { error = run.Failure ?? "create-island failed" });

        var islands = await store.ListAsync(id, ct);
        return Ok(islands.Select(Project));
    }

    [HttpDelete("{id}/islands/{userId:int}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Remove(string id, int userId, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (Runner is not { } runner || Islands is not { } store)
            return StatusCode(503, new { error = "no database configured" });
        if (await runner.TargetAsync(id, ct) is null) return NotFound(new { error = "unknown device" });

        string who = currentUser.DiscordId ?? "?";
        var run = await runner.RunNowAsync(id,
            new DeviceCookbookRequest(DeviceCookbookIds.RemoveIsland, UserId: userId), $"admin:{who}", ct);
        if (!run.Ok) return StatusCode(502, new { error = run.Failure ?? "remove-island failed" });

        var islands = await store.ListAsync(id, ct);
        return Ok(islands.Select(Project));
    }

    private static IslandRow Project(DeviceIsland island) =>
        new(island.UserId, island.Label, island.Provisioned, island.EggAccountId);
}
