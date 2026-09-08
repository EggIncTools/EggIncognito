using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Core.Services.Devices;

public sealed record DeviceCaptureConfig {
    public bool Enabled { get; init; }
    public int BasePort { get; init; } = 9100;
    public string? HostIp { get; init; }
    public bool Verbose { get; init; }

    public string? IosSshHost { get; init; }
    public string IosSshPort { get; init; } = "2222";
    public string? IosSshKeyPath { get; init; }
    public string? IosSetCommand { get; init; }
    public string? IosClearCommand { get; init; }
    public string? IosNetworkServiceGuid { get; init; }
    public string? IosPlutilPath { get; init; }
    public string? IosPreferencesPlist { get; init; }
    public string? IosProxyReloadCommand { get; init; }

    public string? AndroidCaInstallScript { get; init; }
    public string? IosCaInstallCommand { get; init; }
    public string? IosTrustStorePath { get; init; }
    public string? IosAppProcessName { get; init; }
    public string? IosRestartCommand { get; init; }

    public static DeviceCaptureConfig Bind(IConfiguration config) {
        var dc = config.GetSection("DeviceCapture");
        var ios = dc.GetSection("Ios");
        var android = dc.GetSection("Android");
        var upd = config.GetSection("DeviceUpdate").GetSection("Ios");

        return new DeviceCaptureConfig {
            Enabled = Flag(dc, "Enabled", false),
            BasePort = Num(dc, "BasePort", 9100),
            Verbose = Flag(dc, "Verbose", false),
            HostIp = Nz(dc["HostIp"]),
            IosSshHost = Nz(ios["SshHost"]) ?? Nz(upd["SshHost"]),
            IosSshPort = Nz(ios["SshPort"]) ?? Nz(upd["SshPort"]) ?? "2222",
            IosSshKeyPath = Nz(ios["SshKeyPath"]) ?? Nz(upd["SshKeyPath"]),
            IosSetCommand = Nz(ios["SetCommand"]),
            IosClearCommand = Nz(ios["ClearCommand"]),
            IosNetworkServiceGuid = Nz(ios["NetworkServiceGuid"]),
            IosPlutilPath = Nz(ios["PlutilPath"]),
            IosPreferencesPlist = Nz(ios["PreferencesPlist"]),
            IosProxyReloadCommand = Nz(ios["ProxyReloadCommand"]),
            AndroidCaInstallScript = Nz(android["CaInstallScript"]),
            IosCaInstallCommand = Nz(ios["CaInstallCommand"]),
            IosTrustStorePath = Nz(ios["TrustStorePath"]),
            IosAppProcessName = Nz(ios["AppProcessName"]),
            IosRestartCommand = Nz(ios["RestartCommand"])
        };
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static bool Flag(IConfiguration config, string key, bool fallback) {
        string? raw = Nz(config[key]);
        if (raw is null) return fallback;
        return bool.TryParse(raw, out bool parsed) ? parsed : raw == "1";
    }

    private static int Num(IConfiguration config, string key, int fallback) =>
        int.TryParse(Nz(config[key]), CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
}
