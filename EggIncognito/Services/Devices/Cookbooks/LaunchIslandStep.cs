using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class LaunchIslandStep(
    IDeviceConnectionFactory connections,
    IConfiguration config) : CookbookStep {
    private static readonly TimeSpan KeyWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ForegroundWait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const string AntiTamperMarker = "retrieved anti-tamper";
    private const string PlayNagFreeze = "freeze";

    public override string Id => DeviceCookbookIds.LaunchIsland;
    public override string Title => "Launch island";

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
        var switched = await IslandScope.SwitchAsync(conn, userId, ct);
        if (!switched.Ok) return Failed(lines, switched.Note);
        Add(switched.Note ?? $"switched to user {user}");

        string? component = await ResolveLaunchAsync(conn, target.Package, user, ct);
        if (component is null) {
            Add($"{target.Package} is not in user {user}; sharing the device install in");
            var share = await conn.ShellAsync($"pm install-existing --user {user} {target.Package}", ct);
            if (share.ExitCode != 0 || share.Stdout.Contains("failed", StringComparison.OrdinalIgnoreCase))
                return Failed(lines, $"pm install-existing --user {user} failed: {DeviceParsing.TrimNote(share.Stdout + share.Stderr)}; run install-app-island");
            component = await ResolveLaunchAsync(conn, target.Package, user, ct);
        }

        if (component is null)
            return Failed(lines, $"no launch activity for {target.Package} in user {user}");

        await conn.ShellAsync("logcat -c 2>/dev/null", ct);
        Add($"starting {component} in user {user}");
        var start = await conn.ShellAsync($"am start --user {user} -n {component}", ct);
        if (start.ExitCode != 0 || start.Stdout.Contains("Error", StringComparison.Ordinal))
            return Failed(lines, $"am start --user {user} failed: {DeviceParsing.TrimNote(start.Stdout + start.Stderr)}");

        bool keyed = await WaitAntiTamperAsync(conn, Add, ct);
        if (await HandlePlayNagAsync(conn, user, component, Add, ct) is { } nagFailure)
            return Failed(lines, nagFailure);

        return await VerdictAsync(conn, target, user, component, keyed, lines, Add, ct);
    }

    private static async Task<string?> ResolveLaunchAsync(
        IDeviceConnection conn, string package, string user, CancellationToken ct) {
        var resolve = await conn.ShellAsync(
            $"cmd package resolve-activity --brief --user {user} {package} | tail -1", ct);
        string component = resolve.Stdout.Trim();
        return resolve.ExitCode == 0 && component.Contains('/', StringComparison.Ordinal) ? component : null;
    }

    private async Task<bool> WaitAntiTamperAsync(IDeviceConnection conn, Action<string> add, CancellationToken ct) {
        var deadline = DateTimeOffset.UtcNow + KeyWait;
        while (DateTimeOffset.UtcNow < deadline) {
            if (await HasAntiTamperAsync(conn, ct)) {
                add("play released the anti-tamper key");
                return true;
            }

            await Task.Delay(PollInterval, ct);
        }

        return false;
    }

    private static async Task<bool> HasAntiTamperAsync(IDeviceConnection conn, CancellationToken ct) {
        var r = await conn.ShellAsync($"logcat -d 2>/dev/null | grep -i -c {DeviceShell.Quote(AntiTamperMarker)}", ct);
        return int.TryParse(r.Stdout.Trim(), out int hits) && hits > 0;
    }

    private async Task<string?> HandlePlayNagAsync(
        IDeviceConnection conn, string user, string component, Action<string> add,
        CancellationToken ct) {
        string mode = config["Devices:Islands:PlayNag"] is { Length: > 0 } m ? m.Trim() : PlayNagFreeze;
        if (!string.Equals(mode, PlayNagFreeze, StringComparison.OrdinalIgnoreCase)) {
            add($"PlayNag mode '{mode}': leaving the Play account nag to the operator");
            return null;
        }

        var front = await DeviceForeground.ReadAsync(conn, ct);
        if (!front.Is(DeviceForeground.PlayStorePackage)) return null;

        add("freezing Play to clear the account nag, then re-launching the app");
        await conn.ShellAsync($"am force-stop --user {user} {DeviceForeground.PlayStorePackage}", ct);
        var relaunch = await conn.ShellAsync($"am start --user {user} -n {component}", ct);
        if (relaunch.ExitCode != 0 || relaunch.Stdout.Contains("Error", StringComparison.Ordinal))
            return $"re-launch after freezing Play failed: {DeviceParsing.TrimNote(relaunch.Stdout + relaunch.Stderr)}";

        return null;
    }

    private async Task<CookbookStepResult> VerdictAsync(
        IDeviceConnection conn, DeviceTarget target, string user, string component, bool keyed,
        List<string> lines, Action<string> add, CancellationToken ct) {
        var front = await DeviceForeground.WaitAsync(
            conn, target.Package, DeviceForeground.PlayStorePackage, ForegroundWait, ct);
        if (front.Is(DeviceForeground.PlayStorePackage))
            return Failed(lines, $"Play holds the foreground over user {user}; {DeviceForeground.PlayBlockNote}");

        var alive = await conn.ShellAsync($"pidof {target.Package}", ct);
        bool running = alive.Stdout.Trim().Length > 0;
        if (!running)
            return Failed(lines, $"{component} is not running in user {user} (no pid); front is {front.Package ?? "unknown"}");

        if (await CrashedAsync(conn, target.Package, ct))
            return Failed(lines, $"{target.Package} crashed after launch in user {user}");

        add($"foreground: {front.Component ?? front.Package}");
        if (!front.Is(target.Package))
            return Failed(lines, $"{component} is running in user {user} but never took the foreground; front is {front.Package ?? "unknown"}");

        return keyed
            ? Ok(lines, $"launched {component} in island {user}")
            : Ok(lines, $"launched {component} in island {user} (anti-tamper key marker not seen in logcat)");
    }

    private static async Task<bool> CrashedAsync(IDeviceConnection conn, string package, CancellationToken ct) {
        var r = await conn.ShellAsync(
            $"logcat -d -b crash 2>/dev/null | grep -c {DeviceShell.Quote(package)}", ct);
        return int.TryParse(r.Stdout.Trim(), out int hits) && hits > 0;
    }
}
