using EggIncognito.Core.Services.Assets;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Assets;

public sealed class IconDbTier(IServiceProvider services, ILogger<IconDbTier> logger) : IGameAssetTier {
    private DeviceAssetStore? Store => services.GetService(typeof(DeviceAssetStore)) as DeviceAssetStore;
    public int Priority => 0;

    public bool CanHandle(GameAssetKey key) => key.Kind == "icon";

    public async Task<GameAsset?> TryGetAsync(GameAssetKey key, CancellationToken ct) {
        var store = Store;
        if (store is null) return null;
        try {
            var row = await store.GetAsync(DeviceAssetKinds.Icon, key.Name, key.Platform, ct);
            if (row is null) return null;
            return new GameAsset(key, await store.BytesAsync(row, ct), row.ContentType, $"db@{row.Name}", row.UpdatedAt);
        } catch (Exception ex) {
            logger.LogWarning(ex, "icon db read failed {Name}", key.Name);
            return null;
        }
    }

    public async Task PutAsync(GameAsset asset, CancellationToken ct) {
        var store = Store;
        if (store is null) return;
        try {
            await store.PutAsync(asset.Key.Platform ?? DeviceAssetKinds.AnyPlatform, DeviceAssetKinds.Icon,
                asset.Key.Name, asset.Bytes, asset.ContentType, asset.Key.Version, ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "icon db write failed {Name}", asset.Key.Name);
        }
    }
}

public sealed class IconDiskTier(IconAssetCache cache) : IGameAssetTier {
    public int Priority => 10;

    public bool CanHandle(GameAssetKey key) => key.Kind == "icon";

    public Task<GameAsset?> TryGetAsync(GameAssetKey key, CancellationToken ct) {
        byte[]? png = cache.TryGet(key.Name);
        return Task.FromResult(png is null
            ? null
            : new GameAsset(key, png, "image/png", $"disk@{key.Name}", DateTimeOffset.UtcNow));
    }

    public Task PutAsync(GameAsset asset, CancellationToken ct) =>
        cache.PutAsync(asset.Key.Name, asset.Bytes, ct);
}
