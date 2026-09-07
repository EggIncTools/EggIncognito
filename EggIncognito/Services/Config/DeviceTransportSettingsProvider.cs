using EggIdentity.Settings;

namespace EggIncognito.Services.Config;

public sealed class DeviceTransportSettingsProvider : ISettingsProvider {
    private const string Category = "Devices: transport";

    private static readonly IReadOnlyList<SettingDescriptor> Descriptors = [
        new("device_transport.mode", "DeviceTransport__Mode", "Transport mode", Category,
            SettingKind.Enum, ApplyTier.RestartRequired, Sensitivity.Plain) {
            EnumValues = ["Local", "Remote"], Default = "Local"
        },
        new("device_transport.remote_base_url", "DeviceTransport__RemoteBaseUrl", "Remote bridge URL", Category,
            SettingKind.Url, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_transport.api_key", "DeviceTransport__ApiKey", "Remote bridge API key", Category,
            SettingKind.Secret, ApplyTier.RestartRequired, Sensitivity.Secret),
        new("device_transport.bridge_enabled", "DeviceTransport__BridgeEnabled", "Serve the bridge", Category,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "false" },
        new("device_transport.allowed_cidrs", "DeviceTransport__AllowedCidrs", "Bridge allowed CIDRs", Category,
            SettingKind.CidrList, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Comma separated. The stack may also set indexed DeviceTransport__AllowedCidrs__0 entries."
        },
        new("device_transport.bridge_executables", "DeviceTransport__BridgeExecutables", "Bridge executables", Category,
            SettingKind.StringList, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Default = "adb, ssh, scp, ideviceinstaller",
            Description = "Comma separated allowlist for POST /api/bridge/exec. The stack may also set indexed "
                + "DeviceTransport__BridgeExecutables__0 entries."
        },
        new("device_transport.claim_ttl_seconds", "DeviceTransport__ClaimTtlSeconds", "Claim TTL (seconds)", Category,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "900" }
    ];

    public IReadOnlyList<SettingDescriptor> Describe() => Descriptors;
}
