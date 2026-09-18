using EggIdentity.DbClone;
using EggIdentity.Settings;

namespace EggIncognito.Services.Config;

public static class AppSettingsRegistry {
    public static SettingsRegistry Create() => SettingsRegistry.Compose([
        new BootstrapSettingsProvider(),
        new CaptureSettingsProvider(),
        new DeviceSettingsProvider(),
        new DeviceRecertSettingsProvider(),
        new DeviceTransportSettingsProvider(),
        new FakeDeviceSettingsProvider(),
        new IntegrationSettingsProvider(),
        new PlatformSettingsProvider(),
        new RateLimitSettingsProvider(),
        new VirtualDeviceSettingsProvider(),
        EnvironmentSettings.Provider
    ], []);
}
