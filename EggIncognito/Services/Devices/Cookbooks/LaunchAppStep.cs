using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class LaunchAppStep(IDeviceConnectionFactory connections) : CookbookStep {
    private static readonly TimeSpan ForegroundWait = TimeSpan.FromSeconds(25);

    private const string PairipNote = "pairip key import failed; play will not release it to an untrusted device";
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

        await conn.ShellAsync($"pm enable {DeviceForeground.PlayStorePackage} 2>&1", ct);

        Add($"resolving the launch activity for {target.Package}");
        var resolve = await conn.ShellAsync($"cmd package resolve-activity --brief {target.Package} | tail -1", ct);
        string component = resolve.Stdout.Trim();
        if (resolve.ExitCode != 0 || !component.Contains('/', StringComparison.Ordinal)) {
            return Failed(lines,
                $"no launch activity for {target.Package}: {DeviceParsing.TrimNote(resolve.Stdout + resolve.Stderr)}");
        }

        Add($"starting {component}");
        await conn.ShellAsync("logcat -c 2>/dev/null", ct);
        var start = await conn.ShellAsync($"am start -n {component}", ct);
        if (start.ExitCode != 0 || start.Stdout.Contains("Error", StringComparison.Ordinal)) {
            return Failed(lines,
                $"am start failed: {DeviceParsing.TrimNote(start.Stdout + start.Stderr)}");
        }

        var front = await WaitForegroundAsync(conn, target, ct);
        if (front.Is(DeviceForeground.PlayStorePackage)) {
            Add($"play took the foreground: {front.Component ?? front.Package}");
            await ReportPlayReasonAsync(conn, Add, ct);
            return Failed(lines, $"play blocked the launch: {PairipNote}{await CertifyHintAsync(conn, ct)}");
        }

        Add($"foreground: {front.Component ?? DeviceParsing.TrimNote(front.Raw)}");
        if (front.Is(target.Package)) {
            if (await PairipFailedAsync(conn, target.Package, ct)) {
                Add("pairip key import failed");
                return Failed(lines, $"foreground but black: {PairipNote}{await CertifyHintAsync(conn, ct)}");
            }

            return Ok(lines, $"launched {component}");
        }

        var alive = await conn.ShellAsync($"pidof {target.Package}", ct);
        return alive.Stdout.Trim().Length > 0
            ? Failed(lines, $"{component} is running but never took the foreground in {ForegroundWait.TotalSeconds:F0}s; front is {front.Package ?? "unknown"}")
            : Failed(lines, $"{component} exited within {ForegroundWait.TotalSeconds:F0}s of starting; front is {front.Package ?? "unknown"}");
    }

    private static async Task<string> CertifyHintAsync(IDeviceConnection conn, CancellationToken ct) {
        var root = await DeviceRoot.ProbeAsync(conn, ct);
        string? gsf = await GsfIdentity.ReadAsync(conn, root, ct);
        return gsf is { Length: > 0 } ? $"; gsf id {gsf}" : "; no gsf id";
    }

    private static async Task<bool> PairipFailedAsync(IDeviceConnection conn, string package, CancellationToken ct) {
        var r = await conn.ShellAsync(
            $"p=$(pidof {package}); [ -n \"$p\" ] && logcat -d --pid=$p 2>/dev/null "
            + "| grep -c -E 'KeyImportException|KeyNotImported' || echo 0", ct);
        return int.TryParse(r.Stdout.Trim(), out int hits) && hits > 0;
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
