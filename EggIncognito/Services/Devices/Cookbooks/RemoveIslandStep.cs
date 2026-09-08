using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class RemoveIslandStep(
    IServiceScopeFactory scopeFactory,
    IDeviceConnectionFactory connections) : CookbookStep {
    public override string Id => DeviceCookbookIds.RemoveIsland;
    public override string Title => "Remove island";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Task.FromResult(CookbookStepAvailability.No("islands are android-only"));
        if (connections.For(target) is null)
            return Task.FromResult(CookbookStepAvailability.No("no connection for this device"));

        return Task.FromResult(CookbookStepAvailability.Ready);
    }

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        void Add(string line) {
            lines.Add(line);
            context.Progress(line);
        }

        var target = context.Target;
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Skipped(lines, "islands are android-only");
        if (context.UserId is not { } userId)
            return Failed(lines, "no island selected; this step needs a target island user id");
        if (connections.For(target) is not { } conn)
            return Failed(lines, "no connection for this device");

        string user = IslandScope.User(userId);
        await conn.ShellAsync($"am force-stop --user {user} {target.Package}", ct);
        var remove = await conn.ShellAsync($"pm remove-user {user}", ct);
        bool removed = remove.ExitCode == 0
                       && !remove.Stdout.Contains("Error", StringComparison.OrdinalIgnoreCase)
                       && !remove.Stderr.Contains("Error", StringComparison.OrdinalIgnoreCase);
        if (!removed) {
            return Failed(lines,
                $"pm remove-user {user} failed: {DeviceParsing.TrimNote(remove.Stdout + remove.Stderr)}");
        }

        Add($"removed user {user}");
        await DeleteRowAsync(target.Id, userId, Add, ct);
        return Ok(lines, $"island {user} removed");
    }

    private async Task DeleteRowAsync(string deviceId, int userId, Action<string> add, CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceIslandStore)) is not DeviceIslandStore store) {
            add("no database configured, island row not deleted");
            return;
        }

        await store.RemoveAsync(deviceId, userId, ct);
        add($"island {userId} row deleted");
    }
}
