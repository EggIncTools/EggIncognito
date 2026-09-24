namespace EggIncognito.Core.Services.Devices;

public static class AdbHostKey {
    public const string FileName = "adbkey.pub";
    public const string RootHomeKey = "/root/.android/" + FileName;

    public static string? Resolve(VirtualDeviceConfig config, string? androidUserHome) =>
        Resolve(config, androidUserHome, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string? Resolve(VirtualDeviceConfig config, string? androidUserHome, string? userProfile) =>
        ResolveWithSource(config, androidUserHome, userProfile)?.Key;

    public static (string Key, string Source)? ResolveWithSource(VirtualDeviceConfig config, string? androidUserHome) =>
        ResolveWithSource(config, androidUserHome, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static (string Key, string Source)? ResolveWithSource(
        VirtualDeviceConfig config, string? androidUserHome, string? userProfile) {
        if (Nz(config.AdbPublicKey) is { } literal) return (literal, "Devices:Virtual:AdbPublicKey");
        foreach (string path in Candidates(config.AdbPublicKeyPath, androidUserHome, userProfile)) {
            if (ReadKey(path) is { } key) return (key, path);
        }

        return null;
    }

    public static string Label(string key) {
        string[] parts = key.Trim().Split(' ', 2);
        string body = parts[0];
        string comment = parts.Length > 1 ? parts[1].Trim() : "";
        string head = body.Length > 16 ? body[..16] : body;
        return comment.Length > 0 ? $"{head}... ({comment})" : $"{head}...";
    }

    public static IEnumerable<string> Candidates(string? configuredPath, string? androidUserHome, string? userProfile) {
        if (Nz(configuredPath) is { } configured) yield return configured;
        if (Nz(androidUserHome) is { } home) yield return Path.Combine(home, FileName);
        if (Nz(userProfile) is { } profile) yield return Path.Combine(profile, ".android", FileName);
        yield return RootHomeKey;
    }

    private static string? ReadKey(string path) {
        try {
            return File.Exists(path) ? Nz(File.ReadAllText(path)) : null;
        } catch (IOException) {
            return null;
        } catch (UnauthorizedAccessException) {
            return null;
        }
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
