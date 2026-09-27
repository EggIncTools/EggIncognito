using EggIdentity.Settings;

namespace EggIncognito.Services.Config;

public sealed class DeviceSettingsProvider : ISettingsProvider {
    private const string Polling = "Devices: polling";
    private const string Sync = "Devices: sync";
    private const string Capture = "Devices: capture";
    private const string Update = "Devices: store update";

    private static readonly IReadOnlyList<SettingDescriptor> Descriptors = [
        new("device.polling.enabled", "DevicePolling__Enabled", "Polling enabled", Polling,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "true" },
        new("device.polling.interval_minutes", "DevicePolling__IntervalMinutes", "Poll interval (minutes)", Polling,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "30" },
        new("device.polling.harvest_interval_minutes", "DevicePolling__HarvestIntervalMinutes",
            "Harvest interval (minutes)", Polling,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "120" },
        new("device.polling.harvest_settle_seconds", "DevicePolling__HarvestSettleSeconds",
            "Harvest settle (seconds)", Polling,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "10" },
        new("devices.dir", "Devices__Dir", "Device files directory", Polling,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Directory of *.egidevice.* files. Read-only; the app never writes them."
        },
        new("devices.extensions_path", "Devices__Extensions__Path", "Device extensions path", Polling,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "extensions" },
        new("device_probe.timeout_seconds", "DeviceProbe__TimeoutSeconds", "Probe timeout (seconds)", Polling,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Default = "0", Description = "Zero keeps the built-in default."
        },

        new(SettingKeys.DeviceSyncEnabled, "DeviceSync__Enabled", "Sync enabled", Sync,
            SettingKind.Bool, ApplyTier.Live, Sensitivity.Plain) {
            Default = "false", Description = "Re-read every maintenance tick, so changes take effect without a restart."
        },
        new(SettingKeys.DeviceSyncAutoPublish, "DeviceSync__AutoPublish", "Auto publish", Sync,
            SettingKind.Bool, ApplyTier.Live, Sensitivity.Plain) { Default = "true" },
        new(SettingKeys.DeviceSyncRetryBackoffMinutes, "DeviceSync__RetryBackoffMinutes",
            "Retry backoff (minutes)", Sync,
            SettingKind.Number, ApplyTier.Live, Sensitivity.Plain) { Default = "360" },
        new(SettingKeys.DeviceSyncStoreProbeIntervalMinutes, "DeviceSync__StoreProbeIntervalMinutes",
            "Store probe interval (minutes)", Sync,
            SettingKind.Number, ApplyTier.Live, Sensitivity.Plain) { Default = "360" },

        new("device_capture.enabled", "DeviceCapture__Enabled", "Device capture enabled", Capture,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "false" },
        new("device_capture.base_port", "DeviceCapture__BasePort", "Base port", Capture,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "9100" },
        new("device_capture.host_ip", "DeviceCapture__HostIp", "Host IP", Capture,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_capture.verbose", "DeviceCapture__Verbose", "Verbose capture logging", Capture,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "false" },
        new("device_capture.android.ca_install_script", "DeviceCapture__Android__CaInstallScript",
            "Android CA install script", Capture,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_capture.ios.ssh_host", "DeviceCapture__Ios__SshHost", "iOS SSH host", Capture,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Falls back to DeviceUpdate__Ios__SshHost when unset."
        },
        new("device_capture.ios.ssh_port", "DeviceCapture__Ios__SshPort", "iOS SSH port", Capture,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "2222" },
        new("device_capture.ios.ssh_key_path", "DeviceCapture__Ios__SshKeyPath", "iOS SSH key path", Capture,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_capture.ios.set_command", "DeviceCapture__Ios__SetCommand", "iOS proxy set command", Capture,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_capture.ios.clear_command", "DeviceCapture__Ios__ClearCommand", "iOS proxy clear command", Capture,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_capture.ios.network_service_guid", "DeviceCapture__Ios__NetworkServiceGuid",
            "iOS network service GUID", Capture,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_capture.ios.plutil_path", "DeviceCapture__Ios__PlutilPath", "iOS plutil path", Capture,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_capture.ios.preferences_plist", "DeviceCapture__Ios__PreferencesPlist",
            "iOS preferences plist", Capture,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_capture.ios.proxy_reload_command", "DeviceCapture__Ios__ProxyReloadCommand",
            "iOS proxy reload command", Capture,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Run after the proxy plist changes so configd picks it up. Defaults to launchctl kickstart -k system/com.apple.configd when unset."
        },
        new("device_capture.ios.ca_install_command", "DeviceCapture__Ios__CaInstallCommand",
            "iOS CA install command", Capture,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_capture.ios.trust_store_path", "DeviceCapture__Ios__TrustStorePath",
            "iOS trust store path", Capture,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_capture.ios.app_process_name", "DeviceCapture__Ios__AppProcessName",
            "iOS app process name", Capture,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Used by the iOS app restart path. Defaults to \"Egg, Inc.\" in code when unset."
        },
        new("device_capture.ios.restart_command", "DeviceCapture__Ios__RestartCommand",
            "iOS app restart command", Capture,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Supports {bundle} and {proc} placeholders. A built-in command is used when unset."
        },
        new("device_capture.ios.ui_nav_tweak_path", "DeviceCapture__Ios__UiNavTweakPath",
            "iOS UI nav tweak path", Capture,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Default = "/Library/MobileSubstrate/DynamicLibraries/egiuinav.dylib"
        },

        new("device_update.android.drive_command", "DeviceUpdate__Android__DriveCommand",
            "Android drive command", Update,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Default = "am start -a android.intent.action.VIEW -d market://details?id={package}"
        },
        new("device_update.android.poll_seconds", "DeviceUpdate__Android__PollSeconds",
            "Android poll interval (seconds)", Update,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "15" },
        new("device_update.android.poll_attempts", "DeviceUpdate__Android__PollAttempts",
            "Android poll attempts", Update,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "24" },
        new("device_update.android.ui_first_wait_seconds", "DeviceUpdate__Android__UiFirstWaitSeconds",
            "Android first UI wait (seconds)", Update,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "3" },
        new("device_update.android.ui_retry_wait_seconds", "DeviceUpdate__Android__UiRetryWaitSeconds",
            "Android UI retry wait (seconds)", Update,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "2" },
        new("device_update.android.lookup_country", "DeviceUpdate__Android__LookupCountry",
            "Android lookup country", Update,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_update.android.lookup_locale", "DeviceUpdate__Android__LookupLocale",
            "Android lookup locale", Update,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "en" },
        new("device_update.ios.ssh_host", "DeviceUpdate__Ios__SshHost", "iOS SSH host", Update,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_update.ios.ssh_port", "DeviceUpdate__Ios__SshPort", "iOS SSH port", Update,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "2222" },
        new("device_update.ios.ssh_key_path", "DeviceUpdate__Ios__SshKeyPath", "iOS SSH key path", Update,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_update.ios.trigger_path", "DeviceUpdate__Ios__TriggerPath", "iOS trigger path", Update,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Default = "/var/mobile/eggupdate.trigger"
        },
        new("device_update.ios.tweak_path", "DeviceUpdate__Ios__TweakPath", "iOS tweak path", Update,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Default = "/Library/MobileSubstrate/DynamicLibraries/eggupdate.dylib"
        },
        new("device_update.ios.app_id", "DeviceUpdate__Ios__AppId", "iOS App Store id", Update,
            SettingKind.Snowflake, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "993492744" },
        new("device_update.ios.lookup_country", "DeviceUpdate__Ios__LookupCountry", "iOS lookup country", Update,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("device_update.ios.poll_seconds", "DeviceUpdate__Ios__PollSeconds", "iOS poll interval (seconds)", Update,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "15" },
        new("device_update.ios.poll_attempts", "DeviceUpdate__Ios__PollAttempts", "iOS poll attempts", Update,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "24" }
    ];

    public IReadOnlyList<SettingDescriptor> Describe() => Descriptors;
}
