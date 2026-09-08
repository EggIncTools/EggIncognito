using EggIncognito.Core.Services;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using EggIncognito.Services.Admin;
using EggIncognito.Services.Contracts;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Events;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Predictions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Startup;

public static class DataServices {
    public static void AddEndpointAndRouteSources(this WebApplicationBuilder builder, BootFlags boot) {
        builder.Services.AddSingleton(sp => {
            var config = sp.GetRequiredService<IConfiguration>();
            string path = config["EndpointsPath"] ?? Path.Combine(AppContext.BaseDirectory, "Endpoints");
            return new FileEndpointSource(path, sp.GetRequiredService<ILogger<FileEndpointSource>>());
        });
        builder.Services.AddSingleton<IEndpointStore>(sp => new EndpointStore(
            sp.GetRequiredService<FileEndpointSource>(),
            boot.DbEnabled ? sp.GetRequiredService<IServiceScopeFactory>() : null,
            sp.GetRequiredService<ILogger<EndpointStore>>()));

        builder.Services.AddSingleton<RouteCatalog>();
        builder.Services.AddSingleton<AuxbrainSurface>();
        builder.Services.AddSingleton<IRouteCatalog>(sp =>
            new OverlayRouteCatalog(
                new MergedRouteCatalog(
                    sp.GetRequiredService<RouteCatalog>(),
                    boot.DbEnabled ? sp.GetRequiredService<IDbRouteProvider>() : null,
                    boot.DbEnabled ? sp.GetRequiredService<IBinaryRouteProvider>() : null),
                boot.DbEnabled ? sp.GetRequiredService<IRouteOverrideProvider>() : null));
    }

    public static void AddDatabaseServices(this WebApplicationBuilder builder, BootFlags boot) {
        if (!boot.DbEnabled) return;

        builder.Services.AddDbContextPool<EggIncognitoDbContext>(o => o.UseNpgsql(boot.PgConn));
        builder.Services.AddDataProtection()
            .SetApplicationName("EggIncognito")
            .PersistKeysToDbContext<EggIncognitoDbContext>();

        builder.Services.AddScoped<GameBinaryStore>();
        builder.Services.AddScoped<IApkStoreObserver, ApkChangeNotifier>();
        builder.Services.AddScoped<ApkStore>();
        builder.Services.AddScoped<DeviceModuleStore>();
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
        if (!boot.DbEnabled) return;

        builder.Services.AddSingleton<ConsumeObservationRecorder>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ConsumeObservationRecorder>());
        builder.Services.AddScoped<CaptureCredentialStore>();
        builder.Services.AddScoped<CaptureAddressStore>();
        builder.Services.AddScoped<ProtoRegistryStore>();
        builder.Services.AddScoped<StagedProtoStore>();
        builder.Services.AddScoped<AnalyzedFileStore>();
        builder.Services.AddScoped<DeviceStatusStore>();
        builder.Services.AddScoped<IDeviceStatusStore>(sp => sp.GetRequiredService<DeviceStatusStore>());
        builder.Services.AddScoped<FeedSubscriptionStore>();
        builder.Services.AddScoped<IFeedSubscriptionStore>(sp => sp.GetRequiredService<FeedSubscriptionStore>());
        builder.Services.AddScoped<FeedDispatcher>();
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
        builder.Services.AddScoped<IProtoUpsertObserver, ProtoUpsertNotifier>();
        builder.Services.AddScoped<ApiKeyStore>();
        builder.Services.AddScoped<UserThemeStore>();
        builder.Services.AddScoped<BuildBlobStore>();
        builder.Services.AddScoped<ImageBuildStore>();
        builder.Services.AddScoped<IProtoBackfillStore>(sp => sp.GetRequiredService<ProtoRegistryStore>());

        builder.Services.AddHostedService<GameDataAutoRebuildService>();
        builder.Services.AddHostedService<EndpointCatalogAutoRefreshService>();
    }
}
