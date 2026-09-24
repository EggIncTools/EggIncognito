using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Core.Services.Assets;

public readonly record struct GameAssetKey(string Kind, string? Platform, string Name, string? Version = null);

public sealed record GameAsset(
    GameAssetKey Key,
    byte[] Bytes,
    string ContentType,
    string Provenance,
    DateTimeOffset PulledAt);

public interface IGameAssetTier {
    int Priority { get; }
    bool CanHandle(GameAssetKey key);
    Task<GameAsset?> TryGetAsync(GameAssetKey key, CancellationToken ct);
    Task PutAsync(GameAsset asset, CancellationToken ct);
}

public interface IGameAssetOrigin {
    bool CanHandle(GameAssetKey key);
    Task<GameAsset?> FetchAsync(GameAssetKey key, CancellationToken ct);
}

public sealed record GameAssetResult(bool Ok, GameAsset? Asset, string Source, string? Diagnostics) {
    [MemberNotNullWhen(true, nameof(Asset))]
    public bool Ok { get; init; } = Ok;
}

public sealed class GameAssetProvider(
    IEnumerable<IGameAssetTier> tiers,
    IEnumerable<IGameAssetOrigin> origins,
    ILogger<GameAssetProvider>? logger = null) {
    private readonly IReadOnlyList<IGameAssetOrigin> _origins = origins.ToList();
    private readonly IReadOnlyList<IGameAssetTier> _tiers = tiers.OrderBy(t => t.Priority).ToList();

    public async Task<GameAssetResult> GetAsync(GameAssetKey key, CancellationToken ct) {
        var applicable = _tiers.Where(t => t.CanHandle(key)).ToList();

        for (int i = 0; i < applicable.Count; i++) {
            var hit = await applicable[i].TryGetAsync(key, ct);
            if (hit is null) continue;
            for (int j = 0; j < i; j++)
                await SafePutAsync(applicable[j], hit, logger, ct);
            return new GameAssetResult(true, hit, TierName(applicable[i]), null);
        }

        var origin = _origins.FirstOrDefault(o => o.CanHandle(key));
        if (origin is null)
            return new GameAssetResult(false, null, "none", "no cached asset and no origin for this key");

        GameAsset? fetched;
        try {
            fetched = await origin.FetchAsync(key, ct);
        } catch (Exception ex) {
            return new GameAssetResult(false, null, "origin", ex.Message);
        }

        if (fetched is null)
            return new GameAssetResult(false, null, "origin", "origin returned no asset");

        foreach (var tier in applicable)
            await SafePutAsync(tier, fetched, logger, ct);
        return new GameAssetResult(true, fetched, "origin", null);
    }

    public async Task<GameAssetResult> GetCachedAsync(GameAssetKey key, CancellationToken ct) {
        foreach (var tier in _tiers.Where(t => t.CanHandle(key))) {
            var hit = await tier.TryGetAsync(key, ct);
            if (hit is not null) return new GameAssetResult(true, hit, TierName(tier), null);
        }

        return new GameAssetResult(false, null, "none", "not cached");
    }

    private static async Task SafePutAsync(IGameAssetTier tier, GameAsset asset, ILogger? logger,
        CancellationToken ct) {
        try {
            await tier.PutAsync(asset, ct);
        } catch (Exception ex) {
            logger?.LogAssetCacheWriteFailed(ex, TierName(tier), asset.Key.Kind, asset.Key.Name);
        }
    }

    private static string TierName(IGameAssetTier tier) => tier.GetType().Name;
}

internal static partial class GameAssetProviderLog {
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Caching {Kind} asset {Name} into {Tier} failed; the asset was served but not cached")]
    internal static partial void LogAssetCacheWriteFailed(this ILogger logger, Exception ex, string tier, string kind,
        string name);
}
