using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public static class DeviceReboot {
    private static readonly TimeSpan AdbTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BootPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BootProgressInterval = TimeSpan.FromSeconds(20);

    public static async Task<RootAccess?> RebootAsync(
        IDeviceConnection conn, IProcessRunner runner, TimeProvider time, string serial, TimeSpan bootTimeout,
        Func<string, Task> add, CancellationToken ct) {
        await add($"rebooting, waiting up to {bootTimeout.TotalSeconds:F0}s for boot");
        await AdbDeviceConnection.RunAsync(runner, serial, ["reboot"], AdbTimeout, ct);

        var started = time.GetUtcNow();
        var deadline = started + bootTimeout;
        var nextProgress = started + BootProgressInterval;
        while (time.GetUtcNow() < deadline) {
            await Task.Delay(BootPollInterval, ct);
            var boot = await AdbDeviceConnection.RunAsync(runner, serial, ["shell", "getprop sys.boot_completed"], AdbTimeout, ct);

            if (boot.Stdout.Trim() != "1") {
                if (time.GetUtcNow() < nextProgress) continue;
                nextProgress = time.GetUtcNow() + BootProgressInterval;
                await add($"still booting ({(time.GetUtcNow() - started).TotalSeconds:F0}s)");
                continue;
            }

            await add($"boot completed in {(time.GetUtcNow() - started).TotalSeconds:F0}s");
            var root = await DeviceRoot.EnsureAsync(conn, runner, serial, ct, maxAttempts: 18);
            await add($"root after reboot: {root.Detail}");
            return root;
        }

        return null;
    }
}
