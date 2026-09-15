using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class InstallAppIslandStep(
    IServiceScopeFactory scopeFactory,
    IDeviceConnectionFactory connections,
    IProcessRunner runner) : CookbookStep {
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(5);

    public override string Id => DeviceCookbookIds.InstallAppIsland;
    public override string Title => "Install app into island";

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
        if (context.AndroidUserId is not { } androidUserId)
            return Failed(lines, "no island selected; this step needs a target island user id");
        if (connections.For(target) is not { } conn)
            return Failed(lines, "no connection for this device");

        string user = IslandScope.User(androidUserId);
        var path = await conn.ShellAsync($"pm path {target.Package}", ct);
        if (path.ExitCode == 0 && path.Stdout.Contains("package:", StringComparison.Ordinal)) {
            Add($"{target.Package} is on the device; sharing it into user {user}");
            var share = await conn.ShellAsync($"pm install-existing --user {user} {target.Package}", ct);
            if (share.ExitCode == 0 && !share.Stdout.Contains("failed", StringComparison.OrdinalIgnoreCase))
                return Ok(lines, $"shared {target.Package} into island {user}");

            Add($"install-existing did not take: {DeviceParsing.TrimNote(share.Stdout + share.Stderr)}");
        }

        Add($"{target.Package} is not on the device; installing splits into user {user}");
        return await InstallSplitsAsync(target, androidUserId, lines, Add, ct);
    }

    private async Task<CookbookStepResult> InstallSplitsAsync(
        DeviceTarget target, int androidUserId, List<string> lines, Action<string> add, CancellationToken ct) {
        var set = await NewestInstallableAsync(target.Package, ct);
        if (set is null)
            return Failed(lines, $"no stored apk for {target.Package}; run install-app on the owner user first");

        var rows = await SplitsAsync(target.Package, set.AppVersion, set.Build, ct);
        if (rows.Count == 0 || !rows.Any(r => string.Equals(r.Split, ApkSplitNames.Base, StringComparison.OrdinalIgnoreCase)))
            return Failed(lines, $"stored set {set.Key} has no base split");

        var staged = new List<string>();
        try {
            foreach (var row in rows) {
                string path = DeviceShell.NewTempPath($"-{row.Split}.apk");
                await File.WriteAllBytesAsync(path, row.Bytes, ct);
                staged.Add(path);
            }

            add($"installing {rows.Count} split(s) of {set.Key} into user {IslandScope.User(androidUserId)}");
            var install = await Adb(target.Target,
                ["install-multiple", "--user", IslandScope.User(androidUserId), "-r", .. staged], ct);
            if (install.ExitCode != 0) {
                string output = install.Stderr + "\n" + install.Stdout;
                return Failed(lines, $"install-multiple --user failed: {DeviceParsing.TrimNote(output)}");
            }
        } finally {
            foreach (string path in staged) DeviceShell.TryDelete(path);
        }

        return Ok(lines, $"installed {set.Key} into island {IslandScope.User(androidUserId)}");
    }

    private async Task<ApkVersionSet?> NewestInstallableAsync(string package, CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(ApkStore)) is not ApkStore store) return null;
        var sets = await store.VersionsAsync(Platforms.Android, package, ct);
        return sets.FirstOrDefault(s => s.Installable);
    }

    private async Task<IReadOnlyList<CookbookApkSplit>> SplitsAsync(
        string package, string appVersion, string build, CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(ApkStore)) is not ApkStore store) return [];
        var rows = await store.SplitsAsync(Platforms.Android, package, appVersion, build, ct);
        var splits = new List<CookbookApkSplit>(rows.Count);
        foreach (var row in rows) splits.Add(new CookbookApkSplit(row.Split, await store.BytesAsync(row, ct)));
        return splits;
    }

    private async Task<ProcessResult> Adb(string serial, string[] rest, CancellationToken ct) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(InstallTimeout);
        return await runner.RunAsync("adb", ["-s", serial, .. rest], cts.Token);
    }
}
