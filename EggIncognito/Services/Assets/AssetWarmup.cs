using System.Diagnostics;
using EggIncognito.Core.Services.Assets;
using EggIncognito.Services.DataApi;

namespace EggIncognito.Services.Assets;

public sealed class AssetWarmup(IServiceScopeFactory scopes, GameDataStore gameData, ILogger<AssetWarmup> logger)
    : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        await Task.Yield();
        var sw = Stopwatch.StartNew();
        bool ready = gameData.Provider is not null;
        logger.LogInformation("warmup: game data provider {State} in {Ms}ms", ready ? "built" : "unavailable",
            sw.ElapsedMilliseconds);

        sw.Restart();
        using var scope = scopes.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<DataCatalog>();
        var assets = scope.ServiceProvider.GetRequiredService<GameAssetProvider>();
        string? route = catalog.ById("periodical", "get_periodicals")?.WireRoute;
        int fetched = 0, cached = 0, failed = 0;
        foreach (string id in LiveColleggtibleSource.IconIdentifiers(scope.ServiceProvider, route)) {
            if (stoppingToken.IsCancellationRequested) return;
            var key = new GameAssetKey("icon", null, id + "_ce_icon");
            try {
                if ((await assets.GetCachedAsync(key, stoppingToken)).Ok) {
                    cached++;
                    continue;
                }

                if ((await assets.GetAsync(key, stoppingToken)).Ok) fetched++;
                else failed++;
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                failed++;
                logger.LogWarning(ex, "warmup: colleggtible icon {Id} failed", id);
            }
        }

        logger.LogInformation(
            "warmup: colleggtible icons {Cached} cached, {Fetched} fetched, {Failed} failed in {Ms}ms",
            cached, fetched, failed, sw.ElapsedMilliseconds);
    }
}
