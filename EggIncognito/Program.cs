using System.Runtime.CompilerServices;
using EggIdentity.Settings;
using EggIncognito.Build;
using EggIncognito.Capture;
using EggIncognito.Core.Services;
using EggIncognito.Logging;
using EggIncognito.Startup;

[assembly: InternalsVisibleTo("EggIncognito.Tests")]

namespace EggIncognito;

public sealed class Program {
    public static async Task<int> Main(string[] args) {
        if (args.Length >= 3 && args[0] is "__extract-proto" or "__extract-ios-proto")
            return IosProtoExtractor.Run(args[1], args[2]);

        bool captureMode = CaptureLaunchArgs.Apply(args);
        var builder = WebApplication.CreateBuilder(args);
        var fileLogProvider = builder.AddAppHosting();
        var settingsRegistry = builder.AddDbBackedSettings();
        var boot = BootFlags.From(builder);
        RegisterServices(builder, boot, settingsRegistry);

        var app = builder.Build();
        await app.InitializeAsync(boot);
        app.UseAppPipeline(boot);
        await app.RunBotMigrationsAsync(boot);
        app.MapAppEndpoints(boot);
        app.Lifetime.ApplicationStopping.Register(fileLogProvider.Dispose);

        LogStartupState(app, fileLogProvider);
        if (captureMode) app.Lifetime.ApplicationStarted.Register(() => StartCaptureSession(app));
        app.OpenDashboardOnStart(captureMode);

        await app.RunAsync();
        return 0;
    }

    private static void RegisterServices(WebApplicationBuilder builder, BootFlags boot, SettingsRegistry settings) {
        builder.AddWebServices();
        builder.AddAssetServices();
        builder.AddCoreServices();
        builder.AddWorkbenchServices();
        builder.AddEndpointAndRouteSources(boot);
        builder.AddDatabaseServices(boot);
        builder.AddAppSettingsFramework(boot, settings);
        builder.AddDeployServices();
        builder.AddIdentityServices(boot);
        builder.AddVisitsServices(boot);
        builder.AddConsentServices(boot);
        builder.AddBotServices(boot);
        builder.AddSyncIngest(boot);
        builder.AddDatabaseStores(boot);
        builder.AddContributionServices(boot);
        builder.AddCaptureServices(boot);
        builder.AddDeviceServices(boot);
        builder.AddVirtualDeviceServices(boot);
    }

    private static void LogStartupState(WebApplication app, FileLoggerProvider fileLogProvider) {
        bool signing = app.Services.GetRequiredService<ITransportPipeline>().CanSign;
        app.Logger.LogInformation("WebRootPath = {WebRoot}", app.Environment.WebRootPath);
        app.Logger.LogInformation("Request signing: {State} (EGG_INC_API_SALT {SaltState})",
            signing ? "ready" : "DISABLED", signing ? "set" : "not set");
        app.Logger.LogInformation("Log file: {LogFile}", fileLogProvider.FilePath ?? "(file logging disabled)");
    }

    private static void StartCaptureSession(WebApplication app) =>
        _ = app.Services.GetRequiredService<CaptureSession>().StartAsync(CancellationToken.None);
}
