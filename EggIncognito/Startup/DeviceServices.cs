using EggIncognito.Capture;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Devices.Cookbooks;
using EggIncognito.Services.Devices.Fake;
using EggIncognito.Services.Feed;

namespace EggIncognito.Startup;

public static class DeviceServices {
    public static void AddDeviceServices(this WebApplicationBuilder builder, BootFlags boot) {
        var config = builder.Configuration;
        builder.Services.AddSingleton(boot.DeviceConfig);
        builder.Services.AddSingleton(boot.DeviceRecertConfig);
        builder.Services.AddSingleton(boot.GmsFirstRunConfig);
        builder.Services.AddSingleton(boot.DeviceCaptureConfig);
        builder.Services.AddSingleton(boot.DeviceTransportConfig);
        builder.Services.AddSingleton<IDeviceFleet>(sp => new DeviceFleet(
            sp.GetRequiredService<IServiceScopeFactory>(), boot.DeviceConfig,
            boot.DbEnabled && !boot.FakeDevices));
        if (boot.DbEnabled) builder.Services.AddScoped<DeviceRecertService>();

        int probeTimeoutSeconds = config.GetValue("DeviceProbe:TimeoutSeconds", 0);
        if (probeTimeoutSeconds > 0) DeviceProbeTimeout.Value = TimeSpan.FromSeconds(probeTimeoutSeconds);

        if (boot.FakeDevices) {
            builder.Services.AddSingleton(boot.FakeDeviceSettings);
            builder.Services.AddSingleton<FakeDeviceVersions>();
            builder.Services.AddSingleton<FakeFixtureSource>();
            builder.Services.AddSingleton<IProcessRunner, RefusingProcessRunner>();
            builder.Services.AddSingleton<IHostFacts, LocalHostFacts>();
            builder.Services.AddSingleton<IAdbServer>(sp => sp.GetRequiredService<AdbServerHost>());
            builder.Services.AddSingleton<FakeDeviceAgent>();
            builder.Services.AddSingleton<IDeviceAgentClient>(sp => sp.GetRequiredService<FakeDeviceAgent>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<FakeDeviceAgent>());
            if (boot.DbEnabled) builder.Services.AddScoped<DeviceHarvester>();
        } else if (boot.DeviceTransportConfig.Mode == DeviceTransportMode.Remote) {
            builder.Services.AddSingleton<IProcessRunner, BridgeProcessRunner>();
            builder.Services.AddSingleton<IHostFacts, BridgeHostFacts>();
            builder.Services.AddSingleton<IAdbServer, BridgeAdbServer>();
            builder.Services.AddHttpClient<IDeviceAgentClient, DeviceAgentClient>();
        } else {
            builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
            builder.Services.AddSingleton<IHostFacts, LocalHostFacts>();
            builder.Services.AddSingleton<IAdbServer>(sp => sp.GetRequiredService<AdbServerHost>());
            builder.Services.AddHttpClient<IDeviceAgentClient, DeviceAgentClient>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<AdbServerHost>());
        }

        builder.Services.AddSingleton<AdbServerHost>();
        builder.Services.AddHttpClient(BridgeClient.HttpClientName, c => {
            c.Timeout = Timeout.InfiniteTimeSpan;
            c.MaxResponseContentBufferSize = 512L * 1024 * 1024;
        });

        if (boot.DeviceConfig.Enabled) builder.Services.AddHostedService<DeviceMaintenanceService>();

        builder.Services.AddSingleton<DeviceClaimRegistry>();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<IDeviceConnectionFactory, DeviceConnectionFactory>();
        builder.AddDeviceProxyAndCa(boot);
        builder.AddDeviceCapture(boot);
        builder.AddStoreCheckers(boot);
        builder.Services.AddSingleton<IDevicePlatforms, DevicePlatforms>();
        builder.AddDeviceCookbooks(boot);
    }

    private static void AddDeviceCookbooks(this WebApplicationBuilder builder, BootFlags boot) {
        builder.Services.AddSingleton<VirtualDeviceReadinessProbe>();
        builder.Services.AddSingleton<CookbookExecutor>();
        builder.Services.AddSingleton<ModuleFetcher>();
        builder.Services.AddSingleton<PixelFingerprintFetcher>();
        builder.Services.AddSingleton<IntegrityAssets>();
        builder.Services.AddHttpClient(ModuleFetcher.HttpClientName, c => {
            c.Timeout = TimeSpan.FromSeconds(60);
            c.MaxResponseContentBufferSize = 64 * 1024 * 1024;
            c.DefaultRequestHeaders.UserAgent.ParseAdd("EggIncognito-DeviceModules/1.0");
        });

        builder.Services.AddSingleton<InstallAppStep>();
        builder.Services.AddSingleton<InstallCaStep>();
        builder.Services.AddSingleton<LaunchAppStep>();
        builder.Services.AddSingleton<DismissFirstRunStep>();
        builder.Services.AddSingleton<RecertStep>();
        builder.Services.AddSingleton<ReadinessStep>();
        builder.Services.AddSingleton<InstallIntegrityStep>();
        builder.Services.AddSingleton<ActivateIntegrityStep>();
        builder.Services.AddSingleton<SeedAuditStep>();
        builder.Services.AddSingleton<IntegrityAuditStep>();

        builder.Services.AddSingleton<InstallAppCookbook>();
        builder.Services.AddSingleton<InstallCaCookbook>();
        builder.Services.AddSingleton<LaunchAppCookbook>();
        builder.Services.AddSingleton<DismissFirstRunCookbook>();
        builder.Services.AddSingleton<BringUpCookbook>();
        builder.Services.AddSingleton<RecertCookbook>();
        builder.Services.AddSingleton<ReadinessCookbook>();
        builder.Services.AddSingleton<InstallIntegrityCookbook>();
        builder.Services.AddSingleton<ActivateIntegrityCookbook>();
        builder.Services.AddSingleton<SeedAuditCookbook>();
        builder.Services.AddSingleton<IntegrityAuditCookbook>();
        builder.Services.AddSingleton<IDeviceCookbook>(sp => sp.GetRequiredService<InstallAppCookbook>());
        builder.Services.AddSingleton<IDeviceCookbook>(sp => sp.GetRequiredService<InstallCaCookbook>());
        builder.Services.AddSingleton<IDeviceCookbook>(sp => sp.GetRequiredService<LaunchAppCookbook>());
        builder.Services.AddSingleton<IDeviceCookbook>(sp => sp.GetRequiredService<DismissFirstRunCookbook>());
        builder.Services.AddSingleton<IDeviceCookbook>(sp => sp.GetRequiredService<BringUpCookbook>());
        builder.Services.AddSingleton<IDeviceCookbook>(sp => sp.GetRequiredService<RecertCookbook>());
        builder.Services.AddSingleton<IDeviceCookbook>(sp => sp.GetRequiredService<ReadinessCookbook>());
        builder.Services.AddSingleton<IDeviceCookbook>(sp => sp.GetRequiredService<InstallIntegrityCookbook>());
        builder.Services.AddSingleton<IDeviceCookbook>(sp => sp.GetRequiredService<ActivateIntegrityCookbook>());
        builder.Services.AddSingleton<IDeviceCookbook>(sp => sp.GetRequiredService<SeedAuditCookbook>());
        builder.Services.AddSingleton<IDeviceCookbook>(sp => sp.GetRequiredService<IntegrityAuditCookbook>());
        builder.Services.AddSingleton<IDeviceAppLauncher>(sp => sp.GetRequiredService<LaunchAppCookbook>());

        var extensions = DeviceExtensionLoader.Load(
            builder.Services, builder.Configuration, ContentRoot.Resolve(builder.Configuration["ContentRoot"]));
        builder.Services.AddSingleton(extensions);

        builder.Services.AddSingleton<IDeviceCookbooks, DeviceCookbooks>();
        builder.Services.AddSingleton<CookbookCancellations>();
        if (boot.DbEnabled) builder.Services.AddScoped<DeviceCookbookRunner>();
    }

    private static void AddDeviceProxyAndCa(this WebApplicationBuilder builder, BootFlags boot) {
        var capture = boot.DeviceCaptureConfig;
        if (boot.FakeDevices) {
            builder.Services.AddSingleton<IDeviceProxyConfigurator>(new FakeProxyConfigurator(Platforms.Ios));
            builder.Services.AddSingleton<IDeviceProxyConfigurator>(new FakeProxyConfigurator(Platforms.Android));
            builder.Services.AddSingleton<IDeviceCaInstaller>(new FakeCaInstaller(Platforms.Ios));
            builder.Services.AddSingleton<IDeviceCaInstaller>(new FakeCaInstaller(Platforms.Android));
            builder.Services.AddSingleton<FakeCaptureProxyFactory>();
            return;
        }

        builder.Services.AddSingleton<IDeviceProxyConfigurator, AdbProxyConfigurator>();
        builder.Services.AddSingleton<IDeviceProxyConfigurator>(sp =>
            new IosProxyConfigurator(
                sp.GetRequiredService<IProcessRunner>(),
                new IosProxyConfigurator.SshConfig(
                    capture.IosSshHost, capture.IosSshPort, capture.IosSshKeyPath,
                    capture.IosSetCommand, capture.IosClearCommand,
                    capture.IosNetworkServiceGuid, capture.IosPlutilPath,
                    capture.IosPreferencesPlist, capture.IosProxyReloadCommand)));
        builder.Services.AddSingleton<IDeviceCaInstaller>(sp =>
            new AdbCaInstaller(sp.GetRequiredService<IProcessRunner>(), capture.AndroidCaInstallScript));
        builder.Services.AddSingleton<IDeviceCaInstaller>(sp =>
            new IosCaInstaller(
                sp.GetRequiredService<IProcessRunner>(),
                new IosCaInstaller.SshConfig(
                    capture.IosSshHost, capture.IosSshPort, capture.IosSshKeyPath,
                    capture.IosCaInstallCommand, capture.IosTrustStorePath)));
    }

    private static void AddDeviceCapture(this WebApplicationBuilder builder, BootFlags boot) {
        builder.Services.AddSingleton(sp => {
            var config = sp.GetRequiredService<IConfiguration>();
            string contentRoot = ContentRoot.Resolve(config["ContentRoot"]);
            string capturePath = config["CapturePath"] ?? Path.Combine(contentRoot, "captures");
            string caPath = CaptureCaPath.Resolve(config);
            Func<bool, ICaptureProxy>? proxyFactory = boot.FakeDevices
                ? sp.GetRequiredService<FakeCaptureProxyFactory>().Create
                : null;
            return new DeviceCaptureManager(
                boot.DeviceCaptureConfig, sp.GetRequiredService<IDeviceFleet>(), capturePath, caPath, proxyFactory,
                contentRoot,
                sp.GetRequiredService<ILogger<DeviceCaptureManager>>(),
                sp.GetServices<IDeviceCaInstaller>(),
#pragma warning disable IDE0028
                boot.FakeDevices
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : sp.GetRequiredService<DataCatalog>().WireRoutes().ToHashSet(StringComparer.Ordinal),
#pragma warning restore IDE0028
                boot.FakeDevices ? null : sp.GetService<ConfigChangeNotifier>(),
                sp.GetRequiredService<IRouteCatalog>(),
                sp.GetService<ConsumeObservationRecorder>(),
                sp.GetService<IDeviceResponseSources>());
        });
        builder.Services.AddSingleton<IDeviceCaptureStatus>(sp => sp.GetRequiredService<DeviceCaptureManager>());
        builder.Services.AddSingleton<DeviceProxyPusher>();
        builder.Services.AddSingleton<ProxyReachProbe>();
        if (boot.DeviceCaptureConfig.Enabled)
            builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceCaptureManager>());
    }

    private static void AddStoreCheckers(this WebApplicationBuilder builder, BootFlags boot) {
        var config = builder.Configuration;
        builder.Services.AddHttpClient("itunes", c => c.Timeout = TimeSpan.FromSeconds(10));
        builder.Services.AddHttpClient("play", c => {
            c.Timeout = TimeSpan.FromSeconds(15);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36");
            c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        });
        builder.Services.AddHttpClient("carpet", c => {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.MaxResponseContentBufferSize = 32 * 1024 * 1024;
        });
        builder.Services.AddSingleton<KnownVersionRecorder>();
        builder.Services.AddSingleton<IosStoreCatalog>();
        builder.Services.AddSingleton<AndroidStoreCatalog>();

        builder.Services.AddSingleton<IDeviceUiDriver, AndroidUiDriver>();
        string iosUiNavTweakPath = config["DeviceCapture:Ios:UiNavTweakPath"]
                                   ?? "/Library/MobileSubstrate/DynamicLibraries/egiuinav.dylib";
        builder.Services.AddSingleton<IDeviceUiDriver>(sp => new IosUiDriver(
            sp.GetRequiredService<IDeviceConnectionFactory>(), new IosUiDriver.Options(iosUiNavTweakPath)));

        builder.Services.AddSingleton<AndroidPlatform>();

        if (boot.FakeDevices) {
            AddFakePlatform(builder, boot, Platforms.Ios);
            AddFakePlatform(builder, boot, Platforms.Android);
            return;
        }

        builder.Services.AddSingleton<IDeviceStoreChecker>(sp => AndroidChecker(sp, config));
        builder.Services.AddSingleton<IDeviceStoreChecker>(sp => IosChecker(sp, config));

        builder.Services.AddSingleton<IDevicePlatform, IosPlatform>();
        builder.Services.AddSingleton<IDevicePlatform>(sp => sp.GetRequiredService<AndroidPlatform>());
    }

    private static void AddFakePlatform(WebApplicationBuilder builder, BootFlags boot, string platform) {
        builder.Services.AddSingleton<IDeviceStoreChecker>(sp => new FakeStoreChecker(
            platform, boot.FakeDeviceSettings, sp.GetRequiredService<FakeDeviceVersions>(),
            sp.GetRequiredService<FakeFixtureSource>(), sp.GetRequiredService<KnownVersionRecorder>(),
            sp.GetRequiredService<ILogger<FakeStoreChecker>>()));
        builder.Services.AddSingleton<IDevicePlatform>(sp => new FakeDevicePlatform(
            platform, boot.FakeDeviceSettings, sp.GetRequiredService<FakeDeviceVersions>(),
            sp.GetRequiredService<FakeFixtureSource>(), sp.GetRequiredService<ILogger<FakeDevicePlatform>>(),
            sp.GetServices<IDeviceStoreChecker>(), sp.GetServices<IDeviceProxyConfigurator>(),
            sp.GetServices<IDeviceCaInstaller>(), sp.GetServices<IDeviceUiDriver>()));
    }

    private static StoreUpdateOrchestrator AndroidChecker(IServiceProvider sp, ConfigurationManager config) {
        string drive = config["DeviceUpdate:Android:DriveCommand"]
                       ?? config["DeviceCheck:Android:DriveCommand"]
                       ?? "am start -a android.intent.action.VIEW -d market://details?id={package}";
        int pollSeconds = config.GetValue<int?>("DeviceUpdate:Android:PollSeconds")
                          ?? config.GetValue("DeviceCheck:Android:PollSeconds", 15);
        int pollAttempts = config.GetValue<int?>("DeviceUpdate:Android:PollAttempts")
                           ?? config.GetValue("DeviceCheck:Android:PollAttempts", 24);
        int uiFirstWait = config.GetValue("DeviceUpdate:Android:UiFirstWaitSeconds", 3);
        int uiRetryWait = config.GetValue("DeviceUpdate:Android:UiRetryWaitSeconds", 2);
        string? lookupCountry = config["DeviceUpdate:Android:LookupCountry"];
        string? lookupLocale = config["DeviceUpdate:Android:LookupLocale"] ?? "en";
        return new StoreUpdateOrchestrator(
            new AndroidStoreUpdateDriver(
                sp.GetRequiredService<IProcessRunner>(),
                sp.GetRequiredService<IDeviceConnectionFactory>(),
                new AndroidStoreUpdateDriver.Options(drive, uiFirstWait, uiRetryWait, lookupCountry, lookupLocale),
                sp.GetRequiredService<AndroidStoreCatalog>(),
                sp.GetRequiredService<KnownVersionRecorder>(),
                sp.GetServices<IDeviceUiDriver>(),
                sp.GetRequiredService<ILogger<AndroidStoreUpdateDriver>>()),
            new StoreUpdateOrchestrator.Options(pollSeconds, pollAttempts),
            sp.GetRequiredService<KnownVersionRecorder>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("device.storeupdate.android"));
    }

    private static StoreUpdateOrchestrator IosChecker(IServiceProvider sp, ConfigurationManager config) {
        var ios = config.GetSection("DeviceUpdate").GetSection("Ios");
        return new StoreUpdateOrchestrator(
            new IosStoreUpdateDriver(
                sp.GetRequiredService<IProcessRunner>(),
                new IosStoreUpdateDriver.Options(
                    ios["SshHost"], ios["SshPort"] ?? "2222", ios["SshKeyPath"],
                    ios["TriggerPath"] ?? "/var/mobile/eggupdate.trigger",
                    ios["TweakPath"] ?? "/Library/MobileSubstrate/DynamicLibraries/eggupdate.dylib",
                    ios["AppId"] ?? "993492744", ios["LookupCountry"]),
                sp.GetRequiredService<IosStoreCatalog>(),
                sp.GetRequiredService<KnownVersionRecorder>(),
                sp.GetRequiredService<ILogger<IosStoreUpdateDriver>>()),
            new StoreUpdateOrchestrator.Options(ios.GetValue("PollSeconds", 15), ios.GetValue("PollAttempts", 24)),
            sp.GetRequiredService<KnownVersionRecorder>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("device.storeupdate.ios"));
    }
}
