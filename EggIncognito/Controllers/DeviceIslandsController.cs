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
        if (await runner.TargetAsync(id, ct) is not { } target) return NotFound(new { error = "unknown device" });

        return Ok((await ReconciledAsync(store, target, ct)).Select(Project));
    }

    private async Task<IReadOnlyList<DeviceIsland>> ReconciledAsync(
        DeviceIslandStore store, DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android)) return await store.ListAsync(target.Id, ct);
        if (services.GetService(typeof(IDeviceConnectionFactory)) is not IDeviceConnectionFactory factory
            || factory.For(target) is not { } conn)
            return await store.ListAsync(target.Id, ct);

        var users = await conn.ShellAsync("pm list users", ct);
        if (users.ExitCode != 0 || !users.Stdout.Contains("UserInfo{", StringComparison.Ordinal))
            return await store.ListAsync(target.Id, ct);

        return await store.ReconcileAsync(target.Id, users.Stdout, ct);
    }

    [HttpPost("{id}/islands")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Create(string id, [FromBody] CreateIslandRequest? request, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (Runner is not { } runner || Islands is not { } store)
            return StatusCode(503, new { error = "no database configured" });
        if (await runner.TargetAsync(id, ct) is not { } target) return NotFound(new { error = "unknown device" });

        string who = currentUser.DiscordId ?? "?";
        var run = await runner.RunNowAsync(id,
            new DeviceCookbookRequest(DeviceCookbookIds.CreateIsland, request?.Label), $"admin:{who}", ct);
        if (!run.Ok) return StatusCode(502, new { error = run.Failure ?? "create-island failed" });

        return Ok((await ReconciledAsync(store, target, ct)).Select(Project));
    }

    [HttpDelete("{id}/islands/{userId:int}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Remove(string id, int userId, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (Runner is not { } runner || Islands is not { } store)
            return StatusCode(503, new { error = "no database configured" });
        if (await runner.TargetAsync(id, ct) is not { } target) return NotFound(new { error = "unknown device" });

        string who = currentUser.DiscordId ?? "?";
        var run = await runner.RunNowAsync(id,
            new DeviceCookbookRequest(DeviceCookbookIds.RemoveIsland, UserId: userId), $"admin:{who}", ct);
        if (!run.Ok) return StatusCode(502, new { error = run.Failure ?? "remove-island failed" });

        return Ok((await ReconciledAsync(store, target, ct)).Select(Project));
    }

    [HttpGet("{id}/islands/current")]
    public async Task<IActionResult> Current(string id, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (Runner is not { } runner) return StatusCode(503, new { error = "no database configured" });
        if (await runner.TargetAsync(id, ct) is not { } target) return NotFound(new { error = "unknown device" });
        if (!Platforms.Matches(target.Platform, Platforms.Android)) return Ok(new IslandCurrent(IslandScope.Owner));
        if (services.GetService(typeof(IDeviceConnectionFactory)) is not IDeviceConnectionFactory factory
            || factory.For(target) is not { } conn)
            return Ok(new IslandCurrent(IslandScope.Owner));

        var r = await conn.ShellAsync("am get-current-user", ct);
        int uid = r.ExitCode == 0 && int.TryParse(r.Stdout.Trim(), out int u) ? u : IslandScope.Owner;
        return Ok(new IslandCurrent(uid));
    }

    [HttpPost("{id}/islands/{userId:int}/switch")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Switch(string id, int userId, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (userId < 0) return BadRequest(new { error = "user id must be non-negative" });
        if (Runner is not { } runner) return StatusCode(503, new { error = "no database configured" });
        if (await runner.TargetAsync(id, ct) is not { } target) return NotFound(new { error = "unknown device" });
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return StatusCode(501, new { error = "islands are android-only" });
        if (services.GetService(typeof(IDeviceConnectionFactory)) is not IDeviceConnectionFactory factory
            || factory.For(target) is not { } conn)
            return StatusCode(502, new { error = "no connection for device" });

        var r = await IslandScope.SwitchAsync(conn, userId, ct);
        return r.Ok
            ? Ok(new UiActionResult(true, DeviceOutcomes.Label(r), r.Note))
            : StatusCode(502, new { error = r.Note ?? "switch failed" });
    }

    private static IslandRow Project(DeviceIsland island) =>
        new(island.UserId, island.Label, island.Provisioned, island.EggAccountId);
}
