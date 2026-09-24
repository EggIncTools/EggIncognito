using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Runner.Adb;

public interface IAdbClient {
    Task<string> DumpsysPackageAsync(string package, CancellationToken ct);
    Task<string> PullArmApkAsync(string package, string destPath, CancellationToken ct);
}

public sealed class AdbClient(string target) : IAdbClient {
    private readonly AdbDeviceConnection _conn = new(new ProcessRunner(), target);

    public async Task<string> DumpsysPackageAsync(string package, CancellationToken ct) {
        var r = await _conn.ShellAsync($"dumpsys package {package}", ct);
        return r.Stdout + r.Stderr;
    }

    public async Task<string> PullArmApkAsync(string package, string destPath, CancellationToken ct) {
        var pm = await _conn.ShellAsync($"pm path {package}", ct);
        var arm = DeviceParsing.SelectArmSplit(pm.Stdout)
            ?? throw new InvalidOperationException($"no arm split found for {package}");
        var bytes = await _conn.PullBytesAsync(arm, ct)
            ?? throw new InvalidOperationException($"adb pull did not produce a file for {arm}");
        await File.WriteAllBytesAsync(destPath, bytes, ct);
        return destPath;
    }
}
