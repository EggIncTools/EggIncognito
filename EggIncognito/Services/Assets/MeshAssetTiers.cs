using EggIncognito.Core.Services.Assets;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Assets;

public sealed class MeshDbTier(IServiceProvider services, ILogger<MeshDbTier> logger) : IGameAssetTier {
    private DeviceAssetStore? Store => services.GetService(typeof(DeviceAssetStore)) as DeviceAssetStore;
    public int Priority => 0;

    public bool CanHandle(GameAssetKey key) => key.Kind == "mesh";

    public async Task<GameAsset?> TryGetAsync(GameAssetKey key, CancellationToken ct) {
        var store = Store;
        if (store is null) return null;
        try {
            var row = await store.GetAsync(DeviceAssetKinds.Mesh, key.Name, key.Platform, ct);
            if (row is null) return null;
            var decode = RpoMeshDecoder.Decode(await store.BytesAsync(row, ct), row.Name);
            if (!decode.Ok) {
                logger.LogWarning("mesh decode failed {Stem}: {Why}", row.Name, decode.Diagnostics);
                return null;
            }

            return new GameAsset(key with { Platform = row.Platform }, decode.Glb!, "model/gltf-binary",
                $"db@{row.Platform}:{row.Name}", row.UpdatedAt);
        } catch (Exception ex) {
            logger.LogWarning(ex, "mesh db read failed {Stem}", key.Name);
            return null;
        }
    }

    public Task PutAsync(GameAsset asset, CancellationToken ct) => Task.CompletedTask;
}

public sealed class MeshDiskTier(MeshAssetCache cache) : IGameAssetTier {
    public int Priority => 10;

    public bool CanHandle(GameAssetKey key) => key.Kind == "mesh" && key.Platform is not null;

    public Task<GameAsset?> TryGetAsync(GameAssetKey key, CancellationToken ct) {
        byte[]? glb = cache.TryGet(key.Platform!, key.Name);
        return Task.FromResult(glb is null
            ? null
            : new GameAsset(key, glb, "model/gltf-binary", $"disk@{key.Platform}:{key.Name}", DateTimeOffset.UtcNow));
    }

    public Task PutAsync(GameAsset asset, CancellationToken ct) =>
        cache.PutAsync(asset.Key.Platform ?? "db", asset.Key.Name, asset.Bytes, ct);
}
