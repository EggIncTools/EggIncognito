namespace EggIncognito.Core.Services.Devices;

public sealed class IosBinaryPuller(SshDeviceConnection conn) {
    public async Task<byte[]?> PullBinaryAsync(string bundleId, CancellationToken ct) {
        var locate = await conn.ShellAsync(
            $"for app in /private/var/containers/Bundle/Application/*/*.app; do " +
            $"if grep -qa {DeviceShell.Quote(bundleId)} \"$app/Info.plist\" 2>/dev/null; then " +
            $"exe=$({DeviceShell.ReadPlistKey("CFBundleExecutable")}); " +
            $"if [ -n \"$exe\" ] && [ -f \"$app/$exe\" ]; then echo \"$app/$exe\"; break; fi; " +
            $"base=$(basename \"$app\" .app); [ -f \"$app/$base\" ] && echo \"$app/$base\" && break; " +
            $"fi; done", ct);
        if (locate.ExitCode != 0) return null;
        string? binPath = locate.Stdout.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?.Trim();
        return string.IsNullOrEmpty(binPath) ? null : await conn.PullBytesAsync(binPath, ct);
    }
}
