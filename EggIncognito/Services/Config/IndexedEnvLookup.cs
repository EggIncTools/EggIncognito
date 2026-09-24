using System.Globalization;
using EggIdentity.Settings;

namespace EggIncognito.Services.Config;

public static class IndexedEnvLookup {
    public static Func<string, string?> For(SettingsRegistry registry, IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configuration);
        var listKeys = new HashSet<string>(
            registry.All
                .Where(d => d.Kind is SettingKind.StringList or SettingKind.CidrList)
                .Select(d => d.EnvKey),
            StringComparer.Ordinal);
        return envKey => listKeys.Contains(envKey) ? Collapse(configuration, envKey) : null;
    }

    private static string? Collapse(IConfiguration configuration, string envKey) {
        string prefix = envKey.Replace("__", ":", StringComparison.Ordinal) + ":";
        var parts = new List<string>();
        for (int i = 0; ; i++) {
            string? part = configuration[prefix + i.ToString(CultureInfo.InvariantCulture)];
            if (string.IsNullOrWhiteSpace(part)) break;
            parts.Add(part.Trim());
        }

        return parts.Count == 0 ? null : string.Join(',', parts);
    }
}
