using EggIncognito.Controllers;
using EggIncognito.Core.Services;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using EggIncognito.Services.Admin;
using EggIncognito.Services.Contracts;
using EggIncognito.Services.Coverage;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Docs;
using EggIncognito.Services.EggSharpSender;
using EggIncognito.Services.Events;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Inspector;
using EggIncognito.Services.Predictions;
using EggIncognito.Services.Predictions.Training;
using EggIncognito.Services.Routes;
using EggIdentity.Reports;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Startup;

public static class DataServices {
    public static void AddEndpointAndRouteSources(this WebApplicationBuilder builder, BootFlags boot) {
        builder.Services.AddSingleton(sp => {
            var config = sp.GetRequiredService<IConfiguration>();
            string path = config["EndpointsPath"] ?? Path.Combine(AppContext.BaseDirectory, "Endpoints");
            return new FileEndpointSource(path, sp.GetRequiredService<ILogger<FileEndpointSource>>());
        });
        builder.Services.AddSingleton(sp => new EndpointStore(
            sp.GetRequiredService<FileEndpointSource>(),
            boot.DbEnabled ? sp.GetRequiredService<IServiceScopeFactory>() : null,
            sp.GetRequiredService<ILogger<EndpointStore>>()));
        builder.Services.AddSingleton<IEndpointStore>(sp => sp.GetRequiredService<EndpointStore>());

        builder.Services.AddSingleton<RouteCatalog>();
        builder.Services.AddSingleton<AuxbrainSurface>();
        builder.Services.AddSingleton<IRouteCatalog>(sp =>
            new OverlayRouteCatalog(
                new MergedRouteCatalog(
                    sp.GetRequiredService<RouteCatalog>(),
                    boot.DbEnabled ? sp.GetRequiredService<IDbRouteProvider>() : null,
                    boot.DbEnabled ? sp.GetRequiredService<IBinaryRouteProvider>() : null),
                boot.DbEnabled ? sp.GetRequiredService<IRouteOverrideProvider>() : null));
        builder.Services.AddSingleton<INonBinaryRouteCatalog>(sp => new NonBinaryRouteCatalog(
            sp.GetRequiredService<RouteCatalog>(),
            boot.DbEnabled ? sp.GetRequiredService<IDbRouteProvider>() : null,
            boot.DbEnabled ? sp.GetRequiredService<IRouteOverrideProvider>() : null));
        builder.Services.AddSingleton<IMessageRoleIndex>(sp => new MessageRoleIndex(
            sp.GetRequiredService<IProtoReflection>(),
            sp.GetRequiredService<IRouteCatalog>(),
            boot.DbEnabled ? sp.GetRequiredService<IBinaryRouteProvider>() : null));
        builder.Services.AddSingleton<IRouteCatalogReport>(sp => new RouteCatalogReport(
            sp.GetRequiredService<IRouteCatalog>(),
            sp.GetRequiredService<RouteCatalog>(),
            sp.GetRequiredService<INonBinaryRouteCatalog>(),
            boot.DbEnabled ? sp.GetRequiredService<IRouteOverrideProvider>() : null,
            boot.DbEnabled ? sp.GetRequiredService<IBinaryRouteProvider>() : null));
        builder.Services.AddSingleton<IDocUsageIndex>(sp => new DocUsageIndex(
            sp.GetRequiredService<IRouteCatalog>(),
            sp.GetRequiredService<IProtoReflection>(),
            boot.DbEnabled ? sp.GetRequiredService<IBinaryRouteProvider>() : null));
    }

    public static void AddDatabaseServices(this WebApplicationBuilder builder, BootFlags boot) {
        if (!boot.DbEnabled) return;

        builder.Services.AddDbContextPool<EggIncognitoDbContext>(o => o.UseNpgsql(boot.PgConn));
        builder.Services.AddSingleton(
            BlobFileStore.For(ContentRoot.Resolve(builder.Configuration["ContentRoot"])));
        builder.Services.AddSingleton<BlobBytes>();
        builder.Services.AddSingleton(new BlobOffloadGate(
            builder.Configuration.GetValue("Storage:BlobOffloadEnabled", false)));
        builder.Services.AddHostedService<BlobOffloadService>();
        builder.Services.AddHostedService<Services.Assets.AssetWarmup>();
        builder.Services.AddDataProtection()
            .SetApplicationName("EggIncognito")
            .PersistKeysToDbContext<EggIncognitoDbContext>();

        builder.Services.AddScoped<GameBinaryStore>();
        builder.Services.AddScoped<IApkStoreObserver, ApkChangeNotifier>();
        builder.Services.AddScoped<ApkStore>();
        builder.Services.AddScoped<SymbolizedReferenceStore>();
        builder.Services.AddScoped<DeviceAssetStore>();
        builder.Services.AddScoped<DeviceStateStore>();
        builder.Services.AddScoped<DeviceIslandStore>();
        builder.Services.AddScoped<DeviceRegistryPublisher>();
        builder.Services.AddScoped<DeviceJobStore>();
        builder.Services.AddSingleton<DeviceTimelineCache>();
        builder.Services.AddSingleton<IDeviceJobSink>(sp => sp.GetRequiredService<DeviceTimelineCache>());
        builder.Services.AddSingleton<DeviceJobFeed>();
        builder.Services.AddSingleton<DeviceCookbookFeed>();
        builder.Services.AddHostedService(sp => new PgChangeListener(
            boot.PgConn!,
            sp.GetRequiredService<DeviceTimelineCache>(),
            sp.GetRequiredService<AdminNotifier>(),
            sp.GetRequiredService<ILogger<PgChangeListener>>()));
        builder.Services.AddScoped<DbEndpointSource>();
        builder.Services.AddScoped(sp => new DbEndpointSourceMarker(sp.GetRequiredService<DbEndpointSource>()));

        builder.Services.AddScoped<DbRouteProvider>();
        builder.Services.AddSingleton<IDbRouteProvider>(sp =>
            new CachedDbRouteProvider(
                new ScopedDbRouteProvider(sp.GetRequiredService<IServiceScopeFactory>()),
                TimeSpan.FromSeconds(15), null, sp.GetRequiredService<ILogger<CachedDbRouteProvider>>()));

        builder.Services.AddScoped<BinaryRouteProvider>();
        builder.Services.AddSingleton<IBinaryRouteProvider>(sp =>
            new CachedBinaryRouteProvider(
                new ScopedBinaryRouteProvider(sp.GetRequiredService<IServiceScopeFactory>()),
                TimeSpan.FromSeconds(15), null, sp.GetRequiredService<ILogger<CachedBinaryRouteProvider>>()));

        builder.Services.AddSingleton<IRouteOverrideProvider>(sp =>
            new CachedRouteOverrideProvider(
                () => RouteOverrideFetch.All(sp.GetRequiredService<IServiceScopeFactory>()),
                TimeSpan.FromSeconds(15), null, sp.GetRequiredService<ILogger<CachedRouteOverrideProvider>>()));
    }

    public static void AddDatabaseStores(this WebApplicationBuilder builder, BootFlags boot) {
        if (!boot.DbEnabled) {
            var none = new NoArtifactObservations();
            builder.Services.AddSingleton<IArtifactObservationSink>(none);
            builder.Services.AddSingleton<IArtifactObservationQuery>(none);
            builder.Services.AddSingleton<ICoverageMap>(none);
            builder.Services.AddSingleton<DropStats>();
            builder.Services.AddSingleton<IContractReleases>(new NoContractReleases());
            builder.Services.AddSingleton(ConsumptionReportSource.Sources);
            builder.Services.AddReportSource<ConsumptionDropRow, NoConsumptionReportRows>(ConsumptionReportSource.Source);
            return;
        }

        builder.Services.AddSingleton<ConsumeObservationRecorder>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ConsumeObservationRecorder>());
        builder.Services.AddSingleton<IArtifactObservationSink>(sp => sp.GetRequiredService<ConsumeObservationRecorder>());
        builder.Services.AddSingleton<IArtifactObservationQuery, ArtifactObservationQuery>();
        builder.Services.AddSingleton<ICoverageMap, CoverageMapAccess>();
        builder.Services.AddSingleton<DropStats>();
        builder.Services.AddSingleton<IContractReleases, ContractReleaseLookup>();
        builder.Services.AddScoped<ConsumeCoverageService>();
        builder.Services.AddSingleton(ConsumptionReportSource.Sources);
        builder.Services.AddReportSource<ConsumptionDropRow, ConsumptionReportRows>(
            ConsumptionReportSource.Source, ConsumptionReportSource.Snapshot);
        builder.Services.AddHostedService<ConsumptionReportCapsInitializer>();
        builder.Services.AddScoped<CaptureCredentialStore>();
        builder.Services.AddScoped<CaptureAddressStore>();
        builder.Services.AddScoped<ProtoRegistryStore>();
        builder.Services.AddScoped<StagedProtoStore>();
        builder.Services.AddScoped<EnvDesignStore>();
        builder.Services.AddScoped<AnalyzedFileStore>();
        builder.Services.AddScoped<DeviceStatusStore>();
        builder.Services.AddScoped<IDeviceStatusStore>(sp => sp.GetRequiredService<DeviceStatusStore>());
        builder.Services.AddScoped<FeedSubscriptionStore>();
        builder.Services.AddScoped<IFeedSubscriptionStore>(sp => sp.GetRequiredService<FeedSubscriptionStore>());
        builder.Services.AddScoped<FeedDispatcher>();
        builder.Services.AddSingleton<FeedPublisher>();
        builder.Services.AddScoped<FeedSampleSource>();
        builder.Services.AddScoped<GameEventIngestor>();
        builder.Services.AddScoped<GameEventBackfill>();
        builder.Services.AddScoped<EventPredictor>();
        builder.Services.AddSingleton<EventPredictionCache>();
        builder.Services.AddScoped<ContractPredictor>();
        builder.Services.AddSingleton<ContractPredictionCache>();
        builder.Services.AddScoped<ContractIngestor>();
        builder.Services.AddScoped<ContractBackfill>();
        builder.Services.AddSingleton<EventDataVersion>();
        builder.Services.AddSingleton<ContractDataVersion>();
        builder.Services.AddSingleton<PredictionTrainer>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<PredictionTrainer>());
        builder.Services.Configure<MvcOptions>(o => o.Filters.Add<PredictionsReadyFilter>());
        builder.Services.AddScoped<IProtoUpsertObserver, ProtoUpsertNotifier>();
        if (EggSharpAppConfig.FromEnvironment() is { } eggSharpApp) {
            builder.Services.AddSingleton(eggSharpApp);
            builder.Services.AddHttpClient<EggSharpGitHub>();
        }

        builder.Services.AddScoped(sp => new EggSharpProtoSender(
            sp, sp.GetRequiredService<ILogger<EggSharpProtoSender>>(), sp.GetService<EggSharpGitHub>()));
        builder.Services.AddScoped<IProtoUpsertObserver>(sp => sp.GetRequiredService<EggSharpProtoSender>());
        builder.Services.AddScoped(sp => new EggSharpDataSender(
            sp.GetRequiredService<EggIncognitoDbContext>(), sp.GetRequiredService<ILogger<EggSharpDataSender>>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetService<EggSharpGitHub>()));
        builder.Services.AddSingleton<EggSharpDataPusher>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<EggSharpDataPusher>());
        builder.Services.AddScoped<ApiKeyStore>();
        builder.Services.AddScoped<ApiKeyFeatureStore>();
        builder.Services.AddScoped<UserThemeStore>();
        builder.Services.AddScoped<IProtoBackfillStore>(sp => sp.GetRequiredService<ProtoRegistryStore>());

        builder.Services.AddHostedService<GameDataAutoRebuildService>();
        builder.Services.AddHostedService<EndpointCatalogAutoRefreshService>();
    }
}

public sealed class ConsumptionReportCapsInitializer(ReportEngineRegistry registry) : IHostedService {
    public Task StartAsync(CancellationToken cancellationToken) {
        registry.SetCaps(ReportTier.Snapshot, new ReportTierCaps(2, 4000, 400));
        registry.SetCaps(ReportTier.Live, new ReportTierCaps(3, 10000, 1000));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
