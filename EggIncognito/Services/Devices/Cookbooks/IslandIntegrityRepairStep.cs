using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class IslandIntegrityRepairStep(IDeviceConnectionFactory connections, TimeProvider time) : CookbookStep {
    private static readonly TimeSpan CheckinWait = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    public override string Id => "island-integrity-repair";
    public override string Title => "Reset island Play state";

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        Task Add(string line) {
            lines.Add(line);
            return context.Progress(line);
        }

        var target = context.Target;
        if (context.AndroidUserId is not { } androidUserId)
            return Failed(lines, "no island selected; this step needs a target island user id");
        if (connections.For(target) is not { } conn)
            return Failed(lines, "no connection for this device");

        var root = await DeviceRoot.ProbeAsync(conn, ct);
        if (!root.Ok) return Failed(lines, $"device is not rooted ({root.Detail}); repair needs su");

        string user = IslandScope.User(androidUserId);
        var report = await IslandIntegrityProbe.ReadAsync(conn, root, androidUserId, ct);

        if (report.TrickyStore) {
            string[] missing = [.. IslandIntegrityProbe.TargetsFor(target.Package).Where(p => !report.IsTargeted(p))];
            if (missing.Length > 0) {
                string f = IslandIntegrityProbe.TargetFile;
                string append = $"touch {f}; [ -n \"$(tail -c1 {f})\" ] && echo >> {f}; "
                    + string.Join("; ", missing.Select(p => $"echo {p} >> {f}"));
                var wrote = await conn.ShellAsync(root.Wrap(append), ct);
                await Add(wrote.ExitCode == 0
                    ? $"added to TrickyStore target.txt: {string.Join(", ", missing)}"
                    : $"target.txt write failed: {DeviceParsing.TrimNote(wrote.Stdout + wrote.Stderr)}");
            }
        } else {
            await Add("TrickyStore absent; skipping target.txt");
        }

        foreach (string p in IslandIntegrityProbe.PlayPackages) {
            string state = report.IslandPackages.GetValueOrDefault(p, "missing");
            if (state == "missing") {
                var share = await conn.ShellAsync($"pm install-existing --user {user} {p}", ct);
                await Add($"install-existing {p} into user {user}: {DeviceParsing.TrimNote(share.Stdout + share.Stderr)}");
            } else if (state == "disabled") {
                var en = await conn.ShellAsync($"pm enable --user {user} {p}", ct);
                await Add($"enable {p} in user {user}: {DeviceParsing.TrimNote(en.Stdout + en.Stderr)}");
            }
        }

        await conn.ShellAsync($"am force-stop --user {user} {DeviceForeground.PlayStorePackage}", ct);
        var clear = await conn.ShellAsync($"pm clear --user {user} {DeviceForeground.PlayStorePackage}", ct);
        await Add($"cleared Play Store data in user {user}: {DeviceParsing.TrimNote(clear.Stdout + clear.Stderr)}");

        await conn.ShellAsync($"am force-stop --user {user} {IslandIntegrityProbe.GmsPackage}", ct);
        await conn.ShellAsync(root.Wrap("killall com.google.android.gms.unstable 2>/dev/null"), ct);
        await Add($"restarted GMS and DroidGuard for user {user}");

        await conn.ShellAsync(
            $"am broadcast --user {user} -a android.server.checkin.CHECKIN >/dev/null 2>&1", ct);
        string? gsf = await WaitCheckinAsync(conn, root, androidUserId, Add, ct);
        return gsf is null
            ? Failed(lines, $"GMS did not check in for user {user} within {CheckinWait.TotalSeconds:F0}s; sign in to a Google account in the island")
            : Ok(lines, $"user {user} checked in, gsf id {gsf}");
    }

    private async Task<string?> WaitCheckinAsync(
        IDeviceConnection conn, RootAccess root, int androidUserId, Func<string, Task> add, CancellationToken ct) {
        var started = time.GetUtcNow();
        while (true) {
            if (await IslandIntegrityProbe.ReadGsfAsync(conn, root, androidUserId, ct) is { } id) return id;
            var elapsed = time.GetUtcNow() - started;
            if (elapsed >= CheckinWait) return null;
            await add($"waiting for gms check-in ({elapsed.TotalSeconds:F0}s)");
            await Task.Delay(PollInterval, ct);
        }
    }
}
