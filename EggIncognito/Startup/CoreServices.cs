using System.Net;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.Assets;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using EggIncognito.Services.Assets;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Workbench;

namespace EggIncognito.Startup;

public static class CoreServices {
    public static void AddAssetServices(this WebApplicationBuilder builder) {
        builder.Services.AddScoped<ShipShellDownloader>();
        builder.Services.AddSingleton<MeshAssetCache>();
        builder.Services.AddSingleton<IconAssetCache>();
        builder.Services.AddScoped<IDeviceResolver, DeviceResolver>();

        builder.Services.AddScoped<IGameAssetTier, MeshDbTier>();
        builder.Services.AddScoped<IGameAssetTier, MeshDiskTier>();
        builder.Services.AddScoped<IGameAssetTier, ConfigDiskTier>();
        builder.Services.AddScoped<IGameAssetTier, IconDbTier>();
        builder.Services.AddScoped<IGameAssetTier, IconDiskTier>();
        builder.Services.AddScoped<IGameAssetOrigin, IconCdnOrigin>();
        builder.Services.AddScoped<GameAssetProvider>();
        builder.Services.AddScoped<DeviceMeshProvider>();

        builder.Services.AddScoped<GameBinaryProvider>();
        builder.Services.AddScoped<GameDataRebuilder>();
        builder.Services.AddScoped<EndpointCatalogRebuilder>();
    }

    public static void AddCoreServices(this WebApplicationBuilder builder) {
        builder.Services.AddSingleton<GameConfigStore>();
        builder.Services.AddSingleton<ConfigChangeNotifier>();
        builder.Services.AddSingleton<DataCatalog>();
        builder.Services.AddSingleton<ConfigSliceCache>();

        var sealedProxyOptions = SealedProxyOptions.FromConfig(builder.Configuration);
        builder.Services.AddSingleton(sealedProxyOptions);
        builder.Services.AddSingleton<ISealedProxy, SealedProxy>();
        builder.Services.AddHttpClient(SealedProxy.EgressClientName, c => {
            c.DefaultRequestHeaders.Add("User-Agent",
                "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
            c.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler {
            AutomaticDecompression = DecompressionMethods.GZip,
            Proxy = SealedProxy.BuildProxy(sealedProxyOptions),
            UseProxy = SealedProxy.BuildProxy(sealedProxyOptions) is not null
        });

        builder.Services.AddSingleton<IAppMode, AppModeService>();
        builder.Services.AddSingleton<IBehaviorService, BehaviorService>();
        builder.Services.AddSingleton<IProtoReflection, ProtoReflection>();
        builder.Services.AddSingleton<GameDataStore>();
        builder.Services.AddSingleton<IGameDataDocuments>(sp => sp.GetRequiredService<GameDataStore>());
        builder.Services.AddSingleton<FarmPlacementDataProvider>();
        builder.Services.AddSingleton<IDocRegistry, DocRegistry>();
        builder.Services.AddSingleton<ILastKnownProtoSource, LastKnownProtoSource>();
        builder.Services.AddSingleton<IEnumFailover, EnumFailover>();
        builder.Services.AddHostedService<EnumFailoverWarmup>();
        builder.Services.AddSingleton<ITransportPipeline, TransportPipeline>();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(TimeProvider.System);
    }

    public static void AddWorkbenchServices(this WebApplicationBuilder builder) {
        builder.Services.AddWorkbenchStates();
        builder.Services.AddScoped<Services.Theme.ThemeWorkbenchState>();
        builder.Services.AddSingleton<Services.Theme.ThemeCssEmitter>();
        builder.Services.AddScoped<Services.Theme.ViewerThemeLive>();
        builder.Services.AddScoped<Services.Theme.ThemeResolver>();
        builder.Services.AddScoped<Services.Theme.ThemeIdentitySync>();
    }
}
