using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class LaunchAppStep(IDeviceConnectionFactory connections) : CookbookStep {
    private static readonly TimeSpan ForegroundWait = TimeSpan.FromSeconds(25);
    private const string PlayReasonCommand =
        "logcat -d 2>/dev/null | grep -i -E 'finsky|vending|certif|licens|integrity|droidguard' | tail -n 12";

    public override string Id => DeviceCookbookIds.LaunchApp;
    public override string Title => "Launch app";

    public override async Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return CookbookStepAvailability.No("launching by resolved activity is android-only");
        if (connections.For(target) is not { } conn)
            return CookbookStepAvailability.No("no connection for this device");

        var pm = await conn.ShellAsync($"pm path {target.Package}", ct);
        if (pm.ExitCode != 0 || !pm.Stdout.Contains("package:", StringComparison.Ordinal))
            return CookbookStepAvailability.No($"{target.Package} is not installed on this device");

        return CookbookStepAvailability.Ready;
    }

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        void Add(string line) {
            lines.Add(line);
            context.Progress(line);
        }

        var target = context.Target;
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Skipped(lines, "launching by resolved activity is android-only");
        if (connections.For(target) is not { } conn)
            return Failed(lines, "no connection for this device");

        Add($"resolving the launch activity for {target.Package}");
        var resolve = await conn.ShellAsync($"cmd package resolve-activity --brief {target.Package} | tail -1", ct);
        string component = resolve.Stdout.Trim();
        if (resolve.ExitCode != 0 || !component.Contains('/', StringComparison.Ordinal)) {
            return Failed(lines,
                $"no launch activity for {target.Package}: {DeviceParsing.TrimNote(resolve.Stdout + resolve.Stderr)}");
        }

        Add($"starting {component}");
        var start = await conn.ShellAsync($"am start -n {component}", ct);
        if (start.ExitCode != 0 || start.Stdout.Contains("Error", StringComparison.Ordinal)) {
            return Failed(lines,
                $"am start failed: {DeviceParsing.TrimNote(start.Stdout + start.Stderr)}");
        }

        var front = await WaitForegroundAsync(conn, target, ct);
        bool dismissedPlay = false;
        if (front.Is(DeviceForeground.PlayStorePackage)) {
            Add($"play took the foreground: {front.Component ?? front.Package}");
            await ReportPlayReasonAsync(conn, Add, ct);
            Add("closing play and relaunching");
            await conn.ShellAsync($"am force-stop {DeviceForeground.PlayStorePackage}", ct);
            await conn.ShellAsync($"am start -n {component}", ct);
            front = await WaitForegroundAsync(conn, target, ct);
            dismissedPlay = true;
            if (front.Is(DeviceForeground.PlayStorePackage)) {
                return Failed(lines,
                    $"{component} started but {DeviceForeground.PlayBlockNote}; play re-took the foreground after "
                    + $"being closed ({front.Component ?? front.Package}), a hard certification gate rather than a "
                    + "dismissable dialog");
            }
        }

        Add($"foreground: {front.Component ?? DeviceParsing.TrimNote(front.Raw)}");
        if (front.Is(target.Package))
            return Ok(lines, dismissedPlay ? $"launched {component} after closing play" : $"launched {component}");

        var alive = await conn.ShellAsync($"pidof {target.Package}", ct);
        return alive.Stdout.Trim().Length > 0
            ? Failed(lines, $"{component} is running but never took the foreground in {ForegroundWait.TotalSeconds:F0}s; front is {front.Package ?? "unknown"}")
            : Failed(lines, $"{component} exited within {ForegroundWait.TotalSeconds:F0}s of starting; front is {front.Package ?? "unknown"}");
    }

    private static Task<ForegroundWindow> WaitForegroundAsync(
        IDeviceConnection conn, DeviceTarget target, CancellationToken ct) =>
        DeviceForeground.WaitAsync(conn, target.Package, DeviceForeground.PlayStorePackage, ForegroundWait, ct);

    private static async Task ReportPlayReasonAsync(IDeviceConnection conn, Action<string> add, CancellationToken ct) {
        var r = await conn.ShellAsync(PlayReasonCommand, ct);
        string[] output = [.. r.Stdout.Split('\n').Select(l => l.TrimEnd('\r').TrimEnd()).Where(l => l.Length > 0)];
        if (output.Length == 0) {
            add("play reason: nothing matching in logcat");
            return;
        }

        add("play reason:");
        foreach (string line in output) add("  " + line);
    }
}
