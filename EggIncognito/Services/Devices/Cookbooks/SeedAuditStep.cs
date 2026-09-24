using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class SeedAuditStep(IDeviceConnectionFactory connections) : CookbookStep {
    private const int LogTail = 40;

    private static readonly (string Label, string Command)[] ShellProbes = [
        ("image identity", "getprop ro.product.name; getprop ro.build.fingerprint; getprop ro.build.type"),
        ("adb props", "getprop ro.adb.secure; getprop ro.debuggable; getprop ro.secure; getprop sys.boot_completed"),
        ("seed service", "getprop " + IntegritySeed.ServiceProp),
        ("seed rc", "ls -l " + IntegritySeed.RcFile + " 2>&1; cat " + IntegritySeed.RcFile + " 2>&1"),
        ("seed dir", "ls -l " + IntegritySeed.SeedDir + " " + IntegritySeed.ModulesDir + " 2>&1"),
        ("seed state", "cat " + IntegritySeed.StateFile + " 2>&1"),
        ("seed log", "tail -n " + LogTail + " " + IntegritySeed.LogFile + " 2>&1"),
        ("root adb keys", "ls -l " + IntegritySeed.RootAdbKeysFile + " 2>&1; wc -l " + IntegritySeed.RootAdbKeysFile + " 2>&1"),
        ("magisk on path", "ls -l /sbin/magisk /sbin/su /system/etc/init/magisk 2>&1")
    ];

    private static readonly (string Label, string Command)[] RootProbes = [
        ("magisk", "/sbin/magisk -v 2>&1; /sbin/magisk -V 2>&1"),
        ("magisk bin dir", "ls -l /data/adb/magisk 2>&1"),
        ("modules", "ls -l /data/adb/modules /data/adb/modules_update 2>&1"),
        ("seed marker", "ls -l " + IntegritySeed.MarkerDir + " 2>&1"),
        ("device adb keys", "ls -l /data/misc/adb 2>&1; wc -l /data/misc/adb/adb_keys 2>&1"),
        ("chain files", "ls -l " + IntegrityChain.PifModuleDir + " " + IntegrityChain.TrickyStoreDir + " " + IntegrityChain.TeesimDir + " 2>&1"),
        ("init log", "logcat -d -b all 2>/dev/null | grep -i -E 'egi-seed|egi/seed' | tail -n 10")
    ];

    public override string Id => DeviceCookbookIds.SeedAudit;
    public override string Title => "Seed audit";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(Platforms.Matches(target.Platform, Platforms.Android)
            ? CookbookStepAvailability.Ready
            : CookbookStepAvailability.No("the seed audit is android-only"));

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        Task Add(string line) {
            lines.Add(line);
            return context.Progress(line);
        }

        if (connections.For(context.Target) is not { } conn) return Failed(lines, "no connection for this device");

        foreach (var (label, command) in ShellProbes) await ReportAsync(conn, label, command, Add, ct);

        var root = await DeviceRoot.ProbeAsync(conn, ct);
        await Add($"root: {root.Detail}");
        if (root.Ok) {
            foreach (var (label, command) in RootProbes) await ReportAsync(conn, label, root.Wrap(command), Add, ct);
        }

        var seed = IntegritySeed.Parse((await conn.ShellAsync(IntegritySeed.ProbeCommand, ct)).Stdout);
        string verdict = Verdict(seed);
        await Add($"verdict: {verdict}");
        return Ok(lines, verdict);
    }

    private static string Verdict(SeedProbe seed) {
        if (!seed.Ran) return "seed probe did not run over adb; nothing above is trustworthy";
        if (!seed.SeededImage) return $"not a seeded image: {IntegritySeed.SeedScript} is absent";
        return (seed.State, seed.Service) switch {
            (IntegritySeed.StateDone, _) => "seed finished on an earlier boot; the chain should be live",
            (IntegritySeed.StateFailed, _) => $"seed failed; last log line: {seed.LastLog ?? "none"}",
            (IntegritySeed.StateInstalling, _) => $"seed still running; last log line: {seed.LastLog ?? "none"}",
            (null, null) => $"init never started {IntegritySeed.ServiceName}: {IntegritySeed.RcFile} was not loaded",
            (null, IntegritySeed.ServiceStopped) => $"init ran {IntegritySeed.ServiceName} but it exited before writing state",
            (null, var svc) => $"{IntegritySeed.ServiceName} is {svc} and has not written state yet",
            _ => $"unexpected seed state '{seed.State}'"
        };
    }

    private static async Task ReportAsync(
        IDeviceConnection conn, string label, string command, Func<string, Task> add, CancellationToken ct) {
        var r = await conn.ShellAsync(command, ct);
        string[] output = [.. (r.Stdout + "\n" + r.Stderr).Split('\n')
            .Select(l => l.TrimEnd('\r').TrimEnd())
            .Where(l => l.Length > 0)];
        if (output.Length == 0) {
            await add($"{label}: (no output, exit {r.ExitCode})");
            return;
        }

        await add($"{label}:");
        foreach (string line in output) await add("  " + line);
    }
}
