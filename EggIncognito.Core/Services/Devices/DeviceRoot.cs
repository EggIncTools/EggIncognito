namespace EggIncognito.Core.Services.Devices;

public sealed record RootAccess(bool Ok, string? SuBinary, string Detail, bool MountMaster = false) {
    public static readonly RootAccess None = new(false, null, "no uid=0 shell and no working su");

    public string Wrap(string command) =>
        SuBinary is null ? command : $"{SuBinary} -c {ShellQuote(command)}";

    public string WrapMountMaster(string command) =>
        SuBinary is not null && MountMaster ? $"{SuBinary} -mm -c {ShellQuote(command)}" : Wrap(command);

    private static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}

public static class DeviceRoot {
    private const string MagiskSu = "/sbin/su";
    private static readonly string[] SuBinaries = [MagiskSu, "/debug_ramdisk/su", "su", "/system/bin/su"];
    private const int ProbeAttempts = 3;
    private static readonly TimeSpan ProbeRetryDelay = TimeSpan.FromSeconds(2);

    public static async Task<RootAccess> ProbeAsync(IDeviceConnection conn, CancellationToken ct) {
        if (await IsRootShellAsync(conn, ct)) return new RootAccess(true, null, "adb shell is uid=0");
        return await ProbeSuAsync(conn, ct);
    }

    public static async Task<RootAccess> EnsureAsync(
        IDeviceConnection conn, IProcessRunner runner, string serial, CancellationToken ct, int maxAttempts = ProbeAttempts) {
        var probed = await ProbeAsync(conn, ct);
        if (probed.Ok) return probed;

        await AdbDeviceConnection.RunAsync(runner, serial, ["root"], null, ct);
        await runner.RunAsync("adb", ["connect", serial], ct);
        for (int attempt = 1; ; attempt++) {
            probed = await ProbeAsync(conn, ct);
            if (probed.Ok || attempt >= maxAttempts) return probed;
            await Task.Delay(ProbeRetryDelay, ct);
        }
    }

    private static async Task<bool> IsRootShellAsync(IDeviceConnection conn, CancellationToken ct) {
        var r = await conn.ShellAsync("id -u", ct);
        return r.ExitCode == 0 && r.Stdout.Trim() == "0";
    }

    private static async Task<RootAccess> ProbeSuAsync(IDeviceConnection conn, CancellationToken ct) {
        foreach (string su in SuBinaries) {
            var r = await conn.ShellAsync($"{su} -c id", ct);
            if (IsStockSu(r.Stdout + r.Stderr)) continue;
            if (!r.Stdout.Contains("uid=0", StringComparison.Ordinal)) continue;

            bool magisk = su == MagiskSu || await IsMagiskAsync(conn, su, ct);
            return new RootAccess(true, su, magisk ? $"{su} (magisk) reports uid=0" : $"{su} reports uid=0", magisk);
        }

        return RootAccess.None;
    }

    private static bool IsStockSu(string output) =>
        output.Contains("invalid uid/gid", StringComparison.OrdinalIgnoreCase)
        || output.Contains("usage: su", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> IsMagiskAsync(IDeviceConnection conn, string su, CancellationToken ct) {
        var r = await conn.ShellAsync($"{su} -v 2>&1; {su} --version 2>&1", ct);
        return (r.Stdout + r.Stderr).Contains("MAGISK", StringComparison.OrdinalIgnoreCase);
    }
}
