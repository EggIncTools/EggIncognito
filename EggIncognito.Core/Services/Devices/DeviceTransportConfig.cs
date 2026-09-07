namespace EggIncognito.Core.Services.Devices;

public sealed class DeviceTransportConfig {
    public DeviceTransportMode Mode { get; set; } = DeviceTransportMode.Local;
    public string? RemoteBaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public bool BridgeEnabled { get; set; }
    public string[] AllowedCidrs { get; set; } = [];
    public int ClaimTtlSeconds { get; set; } = 900;
    public string[] BridgeExecutables { get; set; } = ["adb", "ssh", "scp", "ideviceinstaller"];
}
