using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public static class HostAdbKey {
    public static async Task<(string Key, string Source)?> ResolveAsync(
        IHostFacts facts, VirtualDeviceConfig config, IConfiguration configuration, CancellationToken ct) {
        var local = AdbHostKey.ResolveWithSource(config, configuration["ANDROID_USER_HOME"]);
        var host = await facts.GetAsync(ct);
        if (host.Value?.AdbPublicKey is not { } key || string.IsNullOrWhiteSpace(key)) return local;

        string trimmed = key.Trim();
        if (local is { } l && string.Equals(l.Key.Trim(), trimmed, StringComparison.Ordinal)) return (trimmed, l.Source);
        return (trimmed, $"the adb server on {host.Value.Hostname}");
    }
}
