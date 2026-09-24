using System.Net;
using EggIdentity.Client;
using EggIncognito.Capture;
using EggIncognito.Core.Services;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using EggIncognito.Services.Admin;
using EggIncognito.Services.Contributions;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Feed;

namespace EggIncognito.Startup;

public static class CaptureServices {
    public static void AddCaptureServices(this WebApplicationBuilder builder, BootFlags boot) {
        builder.Services.AddSingleton(boot.HostedCapture);
        builder.Services.AddSingleton(sp => SessionManager(sp, boot));
        builder.Services.AddSingleton(sp =>
            sp.GetRequiredService<CaptureSessionManager>().GetOrCreate(CaptureSessionManager.LocalKey));
        builder.Services.AddHostedService(sp => new CaptureCountersBridge(
            sp.GetRequiredService<AdminNotifier>(), sp.GetRequiredService<CaptureSessionManager>(),
            boot.DeviceCaptureConfig.Enabled ? sp.GetRequiredService<DeviceCaptureManager>() : null,
            sp.GetRequiredService<ILogger<CaptureCountersBridge>>()));

        if (!boot.HostedCaptureOn) return;

        if (string.IsNullOrWhiteSpace(boot.HostedCapture.AddressSecret)) {
            throw new InvalidOperationException(
                "Capture:AddressSecret must be set when hosted capture is enabled (it is the HMAC key for per-user proxy addresses).");
        }

        builder.Services.AddSingleton(sp => FrontDoor(sp, boot));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ProxyFrontDoor>());
        builder.Services.AddHostedService<CaptureSweeper>();
    }

    private static CaptureSessionManager SessionManager(IServiceProvider sp, BootFlags boot) {
        var config = sp.GetRequiredService<IConfiguration>();
        string contentRoot = ContentRoot.Resolve(config["ContentRoot"]);
        var routeCatalog = sp.GetRequiredService<IRouteCatalog>();
        var manager = new CaptureSessionManager(boot.HostedCapture, (key, basePort, tier) => {
            var liveRoutes = sp.GetRequiredService<DataCatalog>().WireRoutes();
            var writeObserver = sp.GetService<ConfigChangeNotifier>();
            if (key == CaptureSessionManager.LocalKey) {
                string capturePath = config["CapturePath"] ?? Path.Combine(contentRoot, "captures");
                string caPath = CaptureCaPath.Resolve(config);
                var opts = new CaptureSessionOptions(
                    int.TryParse(config["CapturePort"], out int cp) ? cp : 8080,
                    config["EGG_INC_EID"],
                    config["CaptureLabel"],
                    config.GetValue("CaptureOverwrite", false),
                    config.GetValue("CaptureVerbose", false),
                    capturePath,
                    caPath,
                    WriteObserver: writeObserver) { LiveRoutes = liveRoutes };
                return new CaptureSession(contentRoot, opts, catalog: routeCatalog);
            }

            string dir = Path.Combine(Path.GetTempPath(), "eggincognito-hosted-capture", key);
            var kinds = sp.GetRequiredService<ICaptureContributionKinds>();
            var recorder = sp.GetService<ContributionRecorder>();
            var hostedOpts = new CaptureSessionOptions(
                basePort, null, null, false,
                config.GetValue("CaptureVerbose", false),
                dir, Path.Combine(dir, "ca.cer"),
                false,
                writeObserver) {
                LiveRoutes = liveRoutes,
                Tier = tier,
                FullDetailRoutes = kinds.AllRoutes,
                OnContribution = recorder is null ? null : recorder.Record
            };
            return new CaptureSession(contentRoot, hostedOpts,
                verbose => new NativeCaptureProxy(verbose) {
                    LanForwarderEnabled = false,
                    TrustCaInOsStore = false
                }, routeCatalog);
        });

        if (sp.GetService<AdminNotifier>() is { } notifier)
            manager.Changed += () => notifier.Publish(AdminTopics.Sessions);
        return manager;
    }

    private static ProxyFrontDoor FrontDoor(IServiceProvider sp, BootFlags boot) {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("capture.frontdoor");
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        async Task<string?> AddrToUser(IPAddress addr) {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetService<CaptureAddressStore>();
            if (store is null) return null;
            var userId = await store.UserForAddrAsync(addr);
            if (userId is null) return null;
            var identity = scope.ServiceProvider.GetService<IdentityApiClient>();
            if (identity is null) return null;
            var user = await identity.GetAsync(userId.Value, CancellationToken.None);
            return user?.DiscordId;
        }

        return new ProxyFrontDoor(
            boot.HostedCapture,
            sp.GetRequiredService<CaptureSessionManager>(),
            AddrToUser,
            msg => logger.LogInformation("{Message}", msg));
    }
}
