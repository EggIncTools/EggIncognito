using EggIdentity.Auth;
using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Devices.Fake;

namespace EggIncognito.Startup;

public sealed record BootFlags {
    public required string? PgConn { get; init; }
    public required bool DbEnabled { get; init; }
    public required string? IdentityApiUrl { get; init; }
    public required string? IdentityApiSecret { get; init; }
    public required bool IdentityApiEnabled { get; init; }
    public required string? AdminApiSecret { get; init; }
    public required SessionCookieOptions? Session { get; init; }
    public required LocalIdentitySettings? LocalIdentity { get; init; }
    public required bool LocalIdentityOn { get; init; }
    public required AuthState AuthState { get; init; }
    public required bool HostedBehindProxy { get; init; }
    public required string? BotToken { get; init; }
    public required string? EventSecret { get; init; }
    public required HostedCaptureOptions HostedCapture { get; init; }
    public required bool HostedCaptureOn { get; init; }
    public required bool FakeDevices { get; init; }
    public required FakeDeviceSettings FakeDeviceSettings { get; init; }
    public required DeviceConfig DeviceConfig { get; init; }
    public required DeviceCaptureConfig DeviceCaptureConfig { get; init; }
    public required DeviceTransportConfig DeviceTransportConfig { get; init; }
    public required DeviceRecertConfig DeviceRecertConfig { get; init; }
    public required GmsFirstRunConfig GmsFirstRunConfig { get; init; }

    public bool AuthEnabled => AuthState.Enabled;
    public bool BotEnabled => !string.IsNullOrWhiteSpace(BotToken);
    public bool SyncIngestEnabled => !string.IsNullOrWhiteSpace(EventSecret);

    public static BootFlags From(WebApplicationBuilder builder) {
        var config = builder.Configuration;
        string env = builder.Environment.EnvironmentName;
        var appMode = new AppModeService(config).Mode;

        string? pgConn = config.GetConnectionString("Postgres");
        string? identityApiUrl = config[IdentityConfigKeys.ApiUrl];
        string? identityApiSecret = config[IdentityConfigKeys.ApiSecret];
        bool identityApiEnabled =
            !string.IsNullOrWhiteSpace(identityApiUrl) && !string.IsNullOrWhiteSpace(identityApiSecret);
        var session = SessionCookieOptions.FromEnvironment();
        bool sharedAuthActive = identityApiEnabled && session is not null;

        LocalIdentityGate.Guard(env, appMode, config);
        bool localIdentityOn = LocalIdentityGate.IsOn(env, appMode, config, sharedAuthActive);

        FakeDeviceGate.Guard(env, appMode, config);
        bool fakeDevices = FakeDeviceGate.IsOn(env, appMode, config);
        var fakeDeviceSettings = fakeDevices ? FakeDeviceOptions.Bind(config) : FakeDeviceSettings.Empty;
        var deviceConfig = DeviceConfig.Bind(config);
        if (fakeDevices) deviceConfig = deviceConfig with { Devices = FakeDeviceOptions.Entries(fakeDeviceSettings) };

        bool hosted = string.Equals(config["AppMode"], "Hosted", StringComparison.OrdinalIgnoreCase);
        var hostedCapture = HostedCaptureOptions.Bind(config);

        return new BootFlags {
            PgConn = pgConn,
            DbEnabled = !string.IsNullOrWhiteSpace(pgConn),
            IdentityApiUrl = identityApiUrl,
            IdentityApiSecret = identityApiSecret,
            IdentityApiEnabled = identityApiEnabled,
            AdminApiSecret = config["ADMIN_API_SECRET"],
            Session = session,
            LocalIdentity = localIdentityOn ? LocalIdentitySettings.Bind(config) : null,
            LocalIdentityOn = localIdentityOn,
            AuthState = new AuthState(
                identityApiEnabled, config[IdentityConfigKeys.WidgetUrl],
                session?.CookieName ?? "eggidentity_session", localIdentityOn, session is not null),
            HostedBehindProxy = hosted,
            BotToken = config["Discord:BotToken"],
            EventSecret = config["SyncEvent:EventSecret"],
            HostedCapture = hostedCapture,
            HostedCaptureOn = hosted && config.GetValue("HostedCaptureEnabled", false),
            FakeDevices = fakeDevices,
            FakeDeviceSettings = fakeDeviceSettings,
            DeviceConfig = deviceConfig,
            DeviceCaptureConfig = DeviceCaptureConfig.Bind(config),
            DeviceTransportConfig = config.GetSection("DeviceTransport").Get<DeviceTransportConfig>() ?? new(),
            DeviceRecertConfig = config.GetSection("DeviceRecert").Get<DeviceRecertConfig>() ?? new(),
            GmsFirstRunConfig = config.GetSection("GmsFirstRun").Get<GmsFirstRunConfig>() ?? new()
        };
    }
}
