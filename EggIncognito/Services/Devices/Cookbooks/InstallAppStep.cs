using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class InstallAppStep(
    IServiceScopeFactory scopeFactory,
    IDeviceFleet fleet,
    IProcessRunner runner,
    IDeviceConnectionFactory connections) : CookbookStep {
    public const string StorePrefix = "store:";
    public const string DevicePrefix = "device:";
    private static readonly TimeSpan AdbTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(5);
    private static readonly string[] VerifierSettings = ["verifier_verify_adb_installs", "package_verifier_enable"];
    private const string PackageLogCommand =
        "logcat -d -s PackageManager:* PackageInstaller:* PackageInstallerSession:* 2>/dev/null | tail -n 12";

    public override string Id => DeviceCookbookIds.InstallApp;
    public override string Title => "Install app";

    public override async Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return CookbookStepAvailability.No("installing the app is android-only");

        var sets = await StoredSetsAsync(target.Package, ct);
        var sources = await SourceDevicesAsync(target.Id, ct);
        bool anyComplete = sets.Any(s => s.Complete);
        var options = new List<DeviceCookbookOption>();
        foreach (var set in sets) {
            bool recommended = options.Count == 0 && (set.Complete || sources.Count == 0);
            options.Add(new DeviceCookbookOption(StorePrefix + set.Key, set.Label, recommended, set.Detail));
        }

        foreach (var source in sources) {
            bool recommended = !anyComplete && !options.Exists(o => o.Recommended);
            options.Add(new DeviceCookbookOption(
                DevicePrefix + source.Id, $"pull fresh from {source.Label}", recommended,
                anyComplete ? null : "the stored sets lack config splits; a fresh pull stores all of them"));
        }

        if (options.Count == 0) {
            return CookbookStepAvailability.No(
                $"no stored apk for {target.Package} and no physical android device is reachable to pull one from");
        }

        return new CookbookStepAvailability(true, null, "Source", options);
    }

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        void Add(string line) {
            lines.Add(line);
            context.Progress(line);
        }

        var target = context.Target;
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Skipped(lines, "installing the app is android-only");

        string? argument = context.Argument;
        if (string.IsNullOrWhiteSpace(argument)) {
            var described = await DescribeAsync(target, ct);
            var options = described.Options;
            argument = options?.FirstOrDefault(o => o.Recommended)?.Value
                       ?? (options is { Count: > 0 } ? options[0].Value : null);
            if (argument is null) return Failed(lines, described.Unavailable ?? "no install source is available");
            Add($"no source given, using {argument}");
        }

        (var splits, string? error) = argument.StartsWith(DevicePrefix, StringComparison.Ordinal)
            ? await PullAsync(argument[DevicePrefix.Length..], Add, ct)
            : await LoadAsync(Trim(argument, StorePrefix), target, Add, ct);
        if (splits is null || error is not null) return Failed(lines, error ?? "no splits resolved");

        return await InstallAsync(target, splits, lines, Add, ct);
    }

    private async Task<CookbookStepResult> InstallAsync(
        DeviceTarget target, IReadOnlyList<CookbookApkSplit> splits, List<string> lines, Action<string> add,
        CancellationToken ct) {
        var ordered = splits
            .OrderByDescending(s => string.Equals(s.Split, ApkSplitNames.Base, StringComparison.OrdinalIgnoreCase))
            .ThenBy(s => s.Split, StringComparer.Ordinal)
            .ToList();
        if (!ordered.Exists(s => ApkSplitNames.IsConfig(s.Split)))
            add("no config splits (density, locale) in this set; the app installs but renders broken, pull a fresh set from a physical device");

        var staged = new List<string>();
        try {
            foreach (var split in ordered) {
                string path = DeviceShell.NewTempPath($"-{split.Split}.apk");
                await File.WriteAllBytesAsync(path, split.Bytes, ct);
                staged.Add(path);
            }

            await DisableVerificationAsync(target, add, ct);
            add($"installing {ordered.Count} split(s): {string.Join(", ", ordered.Select(s => s.Split))}");
            var install = await Adb(target.Target, ["install-multiple", "-r", .. staged], InstallTimeout, ct);
            if (install.ExitCode != 0) {
                string output = install.Stderr + "\n" + install.Stdout;
                foreach (string line in output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)) add($"adb: {line}");
                await DiagnoseAsync(target, add, ct);
                string reason = FailureLine(output) ?? DeviceParsing.TrimNote(output);
                return Failed(lines, $"install-multiple failed: {reason}");
            }
        } finally {
            foreach (string path in staged) DeviceShell.TryDelete(path);
        }

        add($"{target.Package} installed");
        return Ok(lines, $"installed {ordered.Count} split(s)");
    }

    private static string? FailureLine(string output) =>
        output.Split('\n').Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("Failure [", StringComparison.Ordinal)
                                 || l.Contains("INSTALL_FAILED", StringComparison.Ordinal)
                                 || l.Contains("INSTALL_PARSE_FAILED", StringComparison.Ordinal));

    private async Task DiagnoseAsync(DeviceTarget target, Action<string> add, CancellationToken ct) {
        if (connections.For(target) is not { } conn) return;
        var abi = await conn.ShellAsync("getprop ro.product.cpu.abilist", ct);
        add($"device abis: {DeviceParsing.TrimNote(abi.Stdout)}");
        var df = await conn.ShellAsync("df -h /data 2>/dev/null | tail -n 1", ct);
        add($"/data: {DeviceParsing.TrimNote(df.Stdout)}");
        var log = await conn.ShellAsync(PackageLogCommand, ct);
        foreach (string line in log.Stdout.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            add($"logcat: {DeviceParsing.TrimNote(line)}");
    }

    private async Task DisableVerificationAsync(DeviceTarget target, Action<string> add, CancellationToken ct) {
        var conn = connections.For(target);
        foreach (string setting in VerifierSettings) {
            string command = $"settings put global {setting} 0";
            if (conn is not null) await conn.ShellAsync(command, ct);
            else await Adb(target.Target, ["shell", command], AdbTimeout, ct);
        }

        add("adb install verification disabled");
    }

    private async Task<(IReadOnlyList<CookbookApkSplit>? Splits, string? Error)> PullAsync(
        string sourceId, Action<string> add, CancellationToken ct) {
        var source = (await fleet.EnabledAsync(ct)).FirstOrDefault(d =>
            string.Equals(d.Id, sourceId, StringComparison.Ordinal));
        if (source is null) return (null, $"unknown source device '{sourceId}'");
        if (!Platforms.Matches(source.Platform, Platforms.Android))
            return (null, $"source device '{sourceId}' is not android");
        if (DeviceOrigins.IsVirtual(source.Origin))
            return (null, $"source device '{sourceId}' is virtual; splits come off physical devices only");

        add($"pulling {source.Package} splits from {source.Label}");
        var puller = new DeviceApkPuller(runner);
        var pulled = await puller.PullAllSplitsAsync(source.Target, source.Package, ct);
        if (pulled.Count == 0) return (null, $"could not pull any splits from {source.Id}");
        if (!pulled.Any(p => string.Equals(p.Split, ApkSplitNames.Base, StringComparison.OrdinalIgnoreCase)))
            return (null, $"could not pull the base split from {source.Id}");

        CookbookApkSplit[] splits = [.. pulled.Select(p => new CookbookApkSplit(p.Split, p.Bytes))];
        add($"pulled {splits.Length} split(s): {string.Join(", ", splits.Select(s => s.Split))}");

        (string? appVersion, string? build) = await SourceVersionAsync(source, ct);
        if (appVersion is null || build is null) {
            add($"could not read the installed version off {source.Id}, splits not stored");
        } else {
            await StoreAsync(source, appVersion, build, splits, add, ct);
        }

        return (splits, null);
    }

    private async Task StoreAsync(DeviceEntry source, string appVersion, string build,
        IReadOnlyList<CookbookApkSplit> splits, Action<string> add, CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(ApkStore)) is not ApkStore store) {
            add("no database configured, splits not stored");
            return;
        }

        int stored = 0;
        foreach (var split in splits) {
            if (await store.PutAsync(Platforms.Android, source.Package, appVersion, build, split.Split,
                    split.Bytes, source.Id, ct)) {
                stored++;
            }
        }

        add(stored == 0
            ? $"apk store already had {appVersion} ({build})"
            : $"stored {stored} split(s) as {appVersion} ({build})");
    }

    private async Task<(IReadOnlyList<CookbookApkSplit>? Splits, string? Error)> LoadAsync(
        string key, DeviceTarget target, Action<string> add, CancellationToken ct) {
        int at = key.LastIndexOf('@');
        if (at < 0) return (null, $"malformed stored-apk key '{key}' (expected appVersion@build)");
        string appVersion = key[..at];
        string build = key[(at + 1)..];

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(ApkStore)) is not ApkStore store)
            return (null, "no database configured, there is no apk store to install from");

        var rows = await store.SplitsAsync(Platforms.Android, target.Package, appVersion, build, ct);
        if (rows.Count == 0) return (null, $"the apk store has no splits for {appVersion} ({build})");
        if (!rows.Any(r => string.Equals(r.Split, ApkSplitNames.Base, StringComparison.OrdinalIgnoreCase)))
            return (null, $"the apk store has no base split for {appVersion} ({build})");

        add($"loaded {rows.Count} stored split(s) for {appVersion} ({build})");
        var splits = new List<CookbookApkSplit>(rows.Count);
        foreach (var row in rows) splits.Add(new CookbookApkSplit(row.Split, await store.BytesAsync(row, ct)));
        return (splits, null);
    }

    private async Task<(string? AppVersion, string? Build)> SourceVersionAsync(
        DeviceEntry source, CancellationToken ct) {
        var dump = await Adb(source.Target, ["shell", $"dumpsys package {source.Package}"], AdbTimeout, ct);
        return dump.ExitCode != 0 ? (null, null) : DeviceParsing.AndroidVersion(dump.Stdout);
    }

    private async Task<IReadOnlyList<ApkVersionSet>> StoredSetsAsync(string package, CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(ApkStore)) is not ApkStore store) return [];
        var sets = await store.VersionsAsync(Platforms.Android, package, ct);
        return [.. sets.Where(s => s.Installable)];
    }

    private async Task<IReadOnlyList<DeviceEntry>> SourceDevicesAsync(string excludeId, CancellationToken ct) {
        var candidates = (await fleet.EnabledAsync(ct))
            .Where(d => Platforms.Matches(d.Platform, Platforms.Android)
                        && !DeviceOrigins.IsVirtual(d.Origin)
                        && !string.Equals(d.Id, excludeId, StringComparison.Ordinal))
            .ToList();
        if (candidates.Count == 0) return [];

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceJobStore)) is not DeviceJobStore jobs) return candidates;

        var probes = await jobs.LatestPerDeviceAsync(DeviceJobKinds.Probe, ct);
        var reachable = probes.Where(p => p.Reachable == true).Select(p => p.DeviceId)
            .ToHashSet(StringComparer.Ordinal);
        return [.. candidates.Where(d => reachable.Contains(d.Id))];
    }

    private async Task<ProcessResult> Adb(string serial, string[] rest, TimeSpan timeout, CancellationToken ct) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return await runner.RunAsync("adb", ["-s", serial, .. rest], cts.Token);
    }

    private static string Trim(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
}
