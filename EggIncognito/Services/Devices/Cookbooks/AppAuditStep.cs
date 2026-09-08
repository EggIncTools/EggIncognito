using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class AppAuditStep(IDeviceConnectionFactory connections) : CookbookStep {
    public override string Id => DeviceCookbookIds.AppAudit;
    public override string Title => "App audit";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(Platforms.Matches(target.Platform, Platforms.Android)
            ? CookbookStepAvailability.Ready
            : CookbookStepAvailability.No("the app audit is android-only"));

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        void Add(string line) {
            lines.Add(line);
            context.Progress(line);
        }

        var target = context.Target;
        if (connections.For(target) is not { } conn) return Failed(lines, "no connection for this device");
        string pkg = target.Package;

        foreach (var (label, command) in Probes(pkg)) await ReportAsync(conn, label, command, Add, ct);

        var first = await conn.ShellAsync($"pidof {pkg}", ct);
        await Task.Delay(TimeSpan.FromSeconds(8), ct);
        var second = await conn.ShellAsync($"pidof {pkg}", ct);
        Add($"pid before: {first.Stdout.Trim()}, 8s later: {second.Stdout.Trim()}");
        foreach (var (label, command) in LiveProbes(pkg)) await ReportAsync(conn, label, command, Add, ct);

        var paths = await conn.ShellAsync($"pm path {pkg}", ct);
        var splits = DeviceParsing.ApkPaths(paths.Stdout);
        var configs = DeviceParsing.SelectConfigSplits(paths.Stdout);
        var pid = await conn.ShellAsync($"pidof {pkg}", ct);
        bool alive = pid.Stdout.Trim().Length > 0;
        var crash = await conn.ShellAsync(
            $"logcat -d -b crash 2>/dev/null | grep -i -m 5 '{pkg}'", ct);

        string verdict = Verdict(alive, splits.Count, configs.Count, crash.Stdout.Trim());
        Add($"verdict: {verdict}");
        return Ok(lines, verdict);
    }

    private static string Verdict(bool alive, int splitCount, int configCount, string crash) {
        if (crash.Length > 0) return $"the app crashed: {DeviceParsing.TrimNote(crash)}";
        if (!alive) return "the app process is not running; it exited after launch";
        if (splitCount == 0) return "pm reports no apk paths for the package";
        return configCount == 0
            ? $"only {splitCount} apk(s) installed and none are config splits: no density or language resources, "
              + "which renders a black or broken screen. Reinstall with every split"
            : $"the app is running with {splitCount} apk(s) including {configCount} config split(s); "
              + "a black screen here is rendering or startup, not a missing split";
    }

    private static (string Label, string Command)[] Probes(string pkg) => [
        ("apks", $"pm path {pkg}"),
        ("process", $"pidof {pkg}; dumpsys activity processes 2>/dev/null | grep -m 3 '{pkg}'"),
        ("focus", "dumpsys window 2>/dev/null | grep -E 'mCurrentFocus|mFocusedApp'"),
        ("surface", $"dumpsys SurfaceFlinger 2>/dev/null | grep -i -m 6 '{pkg}'"),
        ("gpu", "getprop ro.hardware.egl; getprop ro.hardware.gralloc; getprop debug.hwui.renderer; "
                + "getprop ro.redroid.gpu_mode; getprop ro.hardware.vulkan"),
        ("abi", "getprop ro.product.cpu.abilist; getprop ro.dalvik.vm.isa.arm64"),
        ("app log", $"logcat -d 2>/dev/null | grep -i -E '{pkg}|egginc|unity|libGL|EGL|OpenGL|ndk_translation' | tail -n 25"),
        ("crash log", $"logcat -d -b crash 2>/dev/null | tail -n 20"),
        ("native crash", "ls -lt /data/tombstones 2>/dev/null | head -n 5"),
        ("play state", $"pm list packages -d | grep -c {DeviceForeground.PlayStorePackage} "
                       + $"| sed 's/^1$/DISABLED/;s/^0$/enabled/'")
    ];

    private static (string Label, string Command)[] LiveProbes(string pkg) => [
        ("app output", $"p=$(pidof {pkg}); [ -n \"$p\" ] && logcat -d --pid=$p 2>/dev/null | tail -n 40 "
                       + "|| echo 'no live process to read'"),
        ("frames", $"dumpsys gfxinfo {pkg} 2>/dev/null | grep -i -E "
                   + "'Total frames|Janky|rendered|Draw|HWUI|not found' | head -n 8"),
        ("threads", $"p=$(pidof {pkg}); [ -n \"$p\" ] && ls /proc/$p/task 2>/dev/null | wc -l"),
        ("wchan", $"p=$(pidof {pkg}); [ -n \"$p\" ] && cat /proc/$p/wchan 2>/dev/null; echo"),
        ("proxy", "settings get global http_proxy"),
        ("egl libs", "ls -l /vendor/lib64/egl /system/lib64/egl 2>&1 | head -n 12"),
        ("translation", $"logcat -d 2>/dev/null | grep -i -E 'ndk_translation|libnb|houdini' | tail -n 8")
    ];

    private static async Task ReportAsync(
        IDeviceConnection conn, string label, string command, Action<string> add, CancellationToken ct) {
        var r = await conn.ShellAsync(command, ct);
        string[] output = [.. (r.Stdout + "\n" + r.Stderr).Split('\n')
            .Select(l => l.TrimEnd('\r').TrimEnd())
            .Where(l => l.Length > 0)];
        if (output.Length == 0) {
            add($"{label}: (no output, exit {r.ExitCode})");
            return;
        }

        add($"{label}:");
        foreach (string line in output) add("  " + line);
    }
}
