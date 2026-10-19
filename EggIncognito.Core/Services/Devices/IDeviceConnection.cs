using System.Collections.Concurrent;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;

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

    private string Key => SshKeyFile.Usable(KeyPath);

    public string[] SshArgs(string command) => ["-p", Port, "-i", Key, .. Opts, $"root@{Host}", command];

    public string[] ScpDownArgs(string remotePath, string localDest) =>
        ["-P", Port, "-i", Key, .. Opts, $"root@{Host}:{remotePath}", localDest];

    public string[] ScpUpArgs(string localPath, string remotePath) =>
        ["-P", Port, "-i", Key, .. Opts, localPath, $"root@{Host}:{remotePath}"];
}

public static class SshKeyFile {
    private static readonly ConcurrentDictionary<string, string> Copies = new(StringComparer.OrdinalIgnoreCase);

    public static string Usable(string keyPath) {
        if (!File.Exists(keyPath)) return keyPath;
        if (Copies.TryGetValue(keyPath, out string? copy) && File.Exists(copy)) return copy;
        return Copies[keyPath] = PrivateCopy(keyPath);
    }

    private static string PrivateCopy(string keyPath) {
        string dest = DeviceShell.NewTempPath(".key");
        byte[] key = File.ReadAllBytes(keyPath);
        if (OperatingSystem.IsWindows()) {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
                FileSystemRights.FullControl, AccessControlType.Allow));
            using var stream = new FileInfo(dest).Create(FileMode.CreateNew, FileSystemRights.FullControl,
                FileShare.None, 4096, FileOptions.None, security);
            stream.Write(key);
        } else {
            File.WriteAllBytes(dest, []);
            File.SetUnixFileMode(dest, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.WriteAllBytes(dest, key);
        }

        return dest;
    }
}

public static class DeviceShell {
    public static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    public const string IosToolPath =
        "PATH=/var/jb/usr/bin:/var/jb/bin:/var/jb/usr/sbin:/var/jb/sbin:/cores/binpack/usr/bin:/cores/binpack/bin:$PATH; export PATH; ";

    public static string LocateIosApp(string bundleId) =>
        IosToolPath +
        $"app=$(for a in /private/var/containers/Bundle/Application/*/*.app; do " +
        $"""grep -qa {Quote(bundleId)} "$a/Info.plist" 2>/dev/null && echo "$a" && break; done); """ +
        """[ -z "$app" ] && exit 3;""";

    public const string BinpackPlutil = "/cores/binpack/usr/bin/plutil";

    public static string ReadPlistKey(string key, string plistExpr = "\"$app/Info.plist\"") =>
        $"plutil -key {key} {plistExpr} 2>/dev/null " +
        $"|| {BinpackPlutil} -key {key} {plistExpr} 2>/dev/null " +
        $"|| grep -a -A1 '<key>{key}</key>' {plistExpr} 2>/dev/null " +
        """| sed -n 's|.*<string>\(.*\)</string>.*|\1|p' | head -1""";

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
