using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public static class DeviceReboot {
    private static readonly TimeSpan AdbTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BootPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BootProgressInterval = TimeSpan.FromSeconds(20);
    private const int RootRetries = 6;

    public static async Task<RootAccess?> RebootAsync(
        IDeviceConnection conn, IProcessRunner runner, TimeProvider time, string serial, TimeSpan bootTimeout,
        Func<string, Task> add, CancellationToken ct) {
        await add($"rebooting, waiting up to {bootTimeout.TotalSeconds:F0}s for boot");
        await Adb(runner, ["-s", serial, "reboot"], ct);

        var started = time.GetUtcNow();
        var deadline = started + bootTimeout;
        var nextProgress = started + BootProgressInterval;
        while (time.GetUtcNow() < deadline) {
            await Task.Delay(BootPollInterval, ct);
            await Adb(runner, ["connect", serial], ct);
            var boot = await Adb(runner, ["-s", serial, "shell", "getprop sys.boot_completed"], ct);
            if (boot.ExitCode != 0 && AdbTcp.LooksDisconnected(boot.Stderr)) {
                await AdbTcp.ReviveAsync(runner, serial, ct);
                boot = await Adb(runner, ["-s", serial, "shell", "getprop sys.boot_completed"], ct);
            }

            if (boot.Stdout.Trim() != "1") {
                if (time.GetUtcNow() < nextProgress) continue;
                nextProgress = time.GetUtcNow() + BootProgressInterval;
                await add($"still booting ({(time.GetUtcNow() - started).TotalSeconds:F0}s)");
                continue;
            }

            await add($"boot completed in {(time.GetUtcNow() - started).TotalSeconds:F0}s");
            RootAccess root = RootAccess.None;
            for (var attempt = 0; attempt < RootRetries; attempt++) {
                root = await DeviceRoot.EnsureAsync(conn, runner, serial, ct);
                if (root.Ok) break;
                await Task.Delay(BootPollInterval, ct);
            }

            await add($"root after reboot: {root.Detail}");
            return root;
        }

        return null;
    }

    private static async Task<ProcessResult> Adb(IProcessRunner runner, string[] args, CancellationToken ct) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(AdbTimeout);
        return await runner.RunAsync("adb", args, cts.Token);
    }
}
