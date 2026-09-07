using EggIdentity.Settings;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Config;

public sealed class VirtualDeviceSettingsProvider : ISettingsProvider {
    private const string Category = "Devices: virtual";

    private static readonly IReadOnlyList<SettingDescriptor> Descriptors = [
        new("devices.virtual.enabled", "Devices__Virtual__Enabled", "Virtual devices enabled", Category,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "false" },
        new("devices.virtual.kind", "Devices__Virtual__Kind", "Provisioner kind", Category,
            SettingKind.Enum, ApplyTier.RestartRequired, Sensitivity.Plain) {
            EnumValues = ["redroid"], Default = "redroid"
        },
        new("devices.virtual.network", "Devices__Virtual__Network", "Docker network", Category,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Network new containers join. Empty asks the docker daemon for this app container's own."
        },
        new(SettingKeys.VirtualImageOverride, "Devices__Virtual__Image", "Container image", Category,
            SettingKind.Text, ApplyTier.Live, Sensitivity.Plain) {
            Default = VirtualDeviceConfig.DefaultImage,
            Description = "Read fresh on every provision, so a change applies to the next device created."
        },
        new("devices.virtual.max_instances", "Devices__Virtual__MaxInstances", "Max instances", Category,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "4" },
        new("devices.virtual.docker_socket", "Devices__Virtual__DockerSocket", "Docker socket", Category,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Default = VirtualDeviceConfig.DefaultSocket
        },
        new("devices.virtual.reconcile_seconds", "Devices__Virtual__ReconcileSeconds",
            "Reconcile interval (seconds)", Category,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "20" },
        new("devices.virtual.require_google_play", "Devices__Virtual__RequireGooglePlay",
            "Require Google Play", Category,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "true" },
        new("devices.virtual.gms_package", "Devices__Virtual__GmsPackage", "GMS package", Category,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Default = VirtualDeviceConfig.DefaultGmsPackage
        },
        new("devices.virtual.adb_public_key_path", "Devices__Virtual__AdbPublicKeyPath",
            "Host adb public key path", Category,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "adbkey.pub of the adb server; the integrity cookbook appends it to /data/misc/adb/adb_keys. "
                + "Falls back to $ANDROID_USER_HOME, ~/.android, then /root/.android."
        },
        new("devices.virtual.adb_public_key", "Devices__Virtual__AdbPublicKey", "Host adb public key", Category,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Literal adbkey.pub contents; wins over the path."
        },
        new("devices.virtual.integrity.enabled", "Devices__Virtual__Integrity__Enabled",
            "Integrity modules enabled", Category,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "false" },
        new("devices.virtual.integrity.refresh_hours", "Devices__Virtual__Integrity__RefreshHours",
            "Integrity refresh (hours)", Category,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "24" },
        new("devices.virtual.integrity.disable_magisk_zygisk", "Devices__Virtual__Integrity__DisableMagiskZygisk",
            "Disable Magisk Zygisk", Category,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "true" },
        new("devices.virtual.integrity.allow_unpinned", "Devices__Virtual__Integrity__AllowUnpinned",
            "Allow unpinned modules", Category,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "false" },
        new("devices.virtual.integrity.keybox_path", "Devices__Virtual__Integrity__KeyboxPath",
            "Operator keybox path", Category,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "keybox.xml pushed over Integrity-Box's shared keybox into tricky_store and teesim on every "
                + "activation. Leave empty to use the shared keybox the module fetches."
        },
        new("devices.virtual.integrity.pixel_product", "Devices__Virtual__Integrity__PixelProduct",
            "Pixel beta product", Category,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Pixel beta product to pin the spoofed identity to (for example oriole_beta); "
                + "empty picks one on first fetch and keeps it."
        },
        new("devices.virtual.integrity.fingerprint_refresh_days", "Devices__Virtual__Integrity__FingerprintRefreshDays",
            "Fingerprint refresh (days)", Category,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Default = "7",
            Description = "How often the cached Pixel beta fingerprint is re-fetched from Google before it expires."
        },
        new("devices.virtual.integrity.keybox_url", "Devices__Virtual__Integrity__KeyboxUrl",
            "Shared keybox URL", Category,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Default = VirtualDeviceConfig.DefaultKeyboxUrl,
            Description = "Where the shared keybox is downloaded from when no operator keybox path is set."
        },
        new("devices.virtual.build.enabled", "Devices__Virtual__Build__Enabled", "Image build enabled", Category,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "false" }
    ];

    public IReadOnlyList<SettingDescriptor> Describe() => Descriptors;
}
