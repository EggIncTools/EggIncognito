using System.Security;

namespace EggIncognito.Core.Services.Devices;

public interface IDeviceConnection {
    string Platform { get; }
    Task<ProcessResult> ShellAsync(string command, CancellationToken ct);
    Task<byte[]?> PullBytesAsync(string remotePath, CancellationToken ct);
    Task<bool> PushFileAsync(string localPath, string remotePath, CancellationToken ct);

    bool SupportsExecOut => false;

    Task<ProcessBytesResult> ExecOutAsync(string command, CancellationToken ct) =>
        Task.FromResult(new ProcessBytesResult(-1, [], "exec-out is not supported by this connection"));

    Task<ProcessHandle?> ExecOutStreamAsync(string command, CancellationToken ct) =>
        Task.FromResult<ProcessHandle?>(null);
}

public sealed record SshEndpoint(string Host, string Port, string KeyPath) {
    private static readonly string[] Opts = ["-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes"];

    public string[] SshArgs(string command) => ["-p", Port, "-i", KeyPath, .. Opts, $"root@{Host}", command];

    public string[] ScpDownArgs(string remotePath, string localDest) =>
        ["-P", Port, "-i", KeyPath, .. Opts, $"root@{Host}:{remotePath}", localDest];

    public string[] ScpUpArgs(string localPath, string remotePath) =>
        ["-P", Port, "-i", KeyPath, .. Opts, localPath, $"root@{Host}:{remotePath}"];
}

public static class DeviceShell {
    public static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    public static string LocateIosApp(string bundleId) =>
        $"app=$(for a in /private/var/containers/Bundle/Application/*/*.app; do " +
        $"grep -qa {Quote(bundleId)} \"$a/Info.plist\" 2>/dev/null && echo \"$a\" && break; done); " +
        $"[ -z \"$app\" ] && exit 3;";

    public static byte[]? ReadTemp(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    public static string NewTempPath(string suffix) =>
        Path.Combine(Path.GetTempPath(), $"egi-{Guid.NewGuid():N}{suffix}");

    public static bool IsTempPath(string path) {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Full(path) is not { } full || Full(Path.GetTempPath()) is not { } temp) return false;

        StringComparison how = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string root = Path.TrimEndingDirectorySeparator(temp) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, how) && Path.GetFileName(full).StartsWith("egi-", how);
    }

    private static string? Full(string path) {
        try {
            return Path.GetFullPath(path);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or SecurityException) {
            return null;
        }
    }

    public static bool TryDelete(string path) {
        try {
            if (File.Exists(path)) File.Delete(path);
            return true;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return false;
        }
    }
}
