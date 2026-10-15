using EggIncognito.Data.Services;
using EggIncognito.Models.Coverage;
using EggIncognito.Services.DataApi;
using EggSharp.GameData;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Coverage;

public sealed class ConsumeCoverageCache(IServiceScopeFactory scopes, GameDataStore gameData) {
    private readonly Lock _gate = new();
    private (DateTimeOffset GameDataVersion, long LatestObservationId)? _key;
    private ConsumeCoverageMap? _map;

    public async Task<ConsumeCoverageMap?> GetAsync(CancellationToken ct) {
        if (GameDataVersion() is not { } gameDataVersion) return null;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();
        long latestObservationId = await db.ArtifactConsumeObservations.AsNoTracking()
            .CountedConsumes()
            .Select(o => o.Id)
            .DefaultIfEmpty(0L)
            .MaxAsync(ct);
        var key = (gameDataVersion, latestObservationId);

        lock (_gate) {
            if (_key == key && _map is not null) return _map;
        }

        var built = await scope.ServiceProvider.GetRequiredService<ConsumeCoverageService>().MapAsync(ct);

        lock (_gate) {
            _key = key;
            _map = built;
        }

        return built;
    }

    private DateTimeOffset? GameDataVersion() =>
        gameData.List().FirstOrDefault(d => d.Id == ArtifactCatalog.DocumentId) is { } info ? info.UpdatedAt : null;
}
