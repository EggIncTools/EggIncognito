using EggIdentity.Auth;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/devices")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("read")]
public sealed class DeviceIslandsController(ICurrentUser currentUser) : ApiControllerBase {
    [HttpGet("{id}/islands")]
    [RequiresDb]
    public async Task<IActionResult> List(string id, [FromServices] DeviceCookbookRunner runner,
        [FromServices] DeviceIslandStore store, [FromServices] IDeviceConnectionFactory? factory,
        CancellationToken ct) {
        if (await runner.TargetAsync(id, ct) is not { } target) return Fail(404, "unknown device");

        return Ok((await ReconciledAsync(store, factory, target, ct)).Select(Project));
    }

    private static async Task<IReadOnlyList<DeviceIsland>> ReconciledAsync(
        DeviceIslandStore store, IDeviceConnectionFactory? factory, DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android)) return await store.ListAsync(target.Id, ct);
        if (factory is null || factory.For(target) is not { } conn) return await store.ListAsync(target.Id, ct);

        var users = await conn.ShellAsync("pm list users", ct);
        if (users.ExitCode != 0 || !users.Stdout.Contains("UserInfo{", StringComparison.Ordinal))
            return await store.ListAsync(target.Id, ct);

        var provisioned = new HashSet<int>();
        foreach ((int androidUserId, _) in DeviceIslandStore.ParseUsers(users.Stdout)) {
            var setup = await conn.ShellAsync($"settings get --user {androidUserId} secure user_setup_complete", ct);
            if (setup.ExitCode == 0 && setup.Stdout.Trim() == "1") provisioned.Add(androidUserId);
        }

        return await store.ReconcileAsync(target.Id, users.Stdout, provisioned, ct);
    }

    [HttpPost("{id}/islands")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Create(string id, [FromBody] CreateIslandRequest? request,
        [FromServices] DeviceCookbookRunner runner, [FromServices] DeviceIslandStore store,
        [FromServices] IDeviceConnectionFactory? factory, CancellationToken ct) {
        if (await runner.TargetAsync(id, ct) is not { } target) return Fail(404, "unknown device");

        string who = currentUser.Current.DiscordId ?? "?";
        var run = await runner.RunNowAsync(id,
            new DeviceCookbookRequest(DeviceCookbookIds.CreateIsland, request?.Label), $"admin:{who}", ct);
        if (!run.Ok) return Fail(502, run.Failure ?? "create-island failed");

        return Ok((await ReconciledAsync(store, factory, target, ct)).Select(Project));
    }

    [HttpDelete("{id}/islands/{androidUserId:int}")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Remove(string id, int androidUserId, [FromServices] DeviceCookbookRunner runner,
        [FromServices] DeviceIslandStore store, [FromServices] IDeviceConnectionFactory? factory,
        CancellationToken ct) {
        if (await runner.TargetAsync(id, ct) is not { } target) return Fail(404, "unknown device");

        string who = currentUser.Current.DiscordId ?? "?";
        var run = await runner.RunNowAsync(id,
            new DeviceCookbookRequest(DeviceCookbookIds.RemoveIsland, AndroidUserId: androidUserId), $"admin:{who}", ct);
        if (!run.Ok) return Fail(502, run.Failure ?? "remove-island failed");

        return Ok((await ReconciledAsync(store, factory, target, ct)).Select(Project));
    }

    [HttpGet("{id}/islands/current")]
    [RequiresDb]
    public async Task<IActionResult> Current(string id, [FromServices] DeviceCookbookRunner runner,
        [FromServices] IDeviceConnectionFactory? factory, CancellationToken ct) {
        if (await runner.TargetAsync(id, ct) is not { } target) return Fail(404, "unknown device");
        if (!Platforms.Matches(target.Platform, Platforms.Android)) return Ok(new IslandCurrent(IslandScope.Owner));
        if (factory is null || factory.For(target) is not { } conn) return Ok(new IslandCurrent(IslandScope.Owner));

        var r = await conn.ShellAsync("am get-current-user", ct);
        int current = r.ExitCode == 0 && int.TryParse(r.Stdout.Trim(), out int u) ? u : IslandScope.Owner;
        return Ok(new IslandCurrent(current));
    }

    [HttpPost("{id}/islands/{androidUserId:int}/switch")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Switch(string id, int androidUserId, [FromServices] DeviceCookbookRunner runner,
        [FromServices] IDeviceConnectionFactory? factory, CancellationToken ct) {
        if (androidUserId < 0) return Fail(400, "android user id must be non-negative");
        if (await runner.TargetAsync(id, ct) is not { } target) return Fail(404, "unknown device");
        if (!Platforms.Matches(target.Platform, Platforms.Android)) return Fail(501, "islands are android-only");
        if (factory is null || factory.For(target) is not { } conn) return Fail(502, "no connection for device");

        var r = await IslandScope.SwitchAsync(conn, androidUserId, ct);
        return r.Ok
            ? Ok(new UiActionResult(true, DeviceOutcomes.Label(r), r.Note))
            : Fail(502, r.Note ?? "switch failed");
    }

    private static IslandRow Project(DeviceIsland island) =>
        new(island.AndroidUserId, island.Label, island.Provisioned, island.EggAccountId);
}
