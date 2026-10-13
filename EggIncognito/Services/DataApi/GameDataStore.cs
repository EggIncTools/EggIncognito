using System.Text;
using EggIncognito.Data.Services;
using EggSharp.GameData;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.DataApi;

public sealed record GameDataDocInfo(string Id, DateTimeOffset UpdatedAt, int Bytes);

public sealed class GameDataStore(IServiceScopeFactory scopeFactory, ILogger<GameDataStore> logger)
    : IGameDataDocuments {
    private readonly Lock _gate = new();
    private (int Count, DateTimeOffset MaxUpdated)? _stamp;
    private IGameDataProvider? _cached;

    public IGameDataProvider? Provider => Resolve();

    private IGameDataProvider? Resolve() {
        lock (_gate) {
            var stamp = QueryStamp();
            if (stamp is null) {
                _stamp = null;
                _cached = null;
                return null;
            }

            if (_stamp == stamp) return _cached;
            _cached = BuildProvider();
            _stamp = stamp;
            return _cached;
        }
    }

    public string? Doc(string id) =>
        WithDb(db => db.GameDataDocuments.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => d.Json)
            .FirstOrDefault());

    public IReadOnlyList<string> MissingIds() {
        var present = WithDb(db => db.GameDataDocuments.AsNoTracking().Select(d => d.Id).ToHashSet(StringComparer.Ordinal))
                      ?? [];
        return [.. GameDataProvider.RequiredDocumentIds.Where(id => !present.Contains(id))];
    }

    public IReadOnlyList<GameDataDocInfo> List() =>
        WithDb(db => (IReadOnlyList<GameDataDocInfo>)[
            .. db.GameDataDocuments.AsNoTracking()
                .Select(d => new { d.Id, d.UpdatedAt, d.Json })
                .AsEnumerable()
                .Select(d => new GameDataDocInfo(d.Id, d.UpdatedAt, Encoding.UTF8.GetByteCount(d.Json)))
                .OrderBy(d => d.Id, StringComparer.Ordinal)
        ]) ?? [];

    private (int Count, DateTimeOffset MaxUpdated)? QueryStamp() =>
        WithDb<(int, DateTimeOffset)?>(db => {
            var row = db.GameDataDocuments.AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Max = g.Max(d => d.UpdatedAt) })
                .FirstOrDefault();
            return row is null ? (0, DateTimeOffset.MinValue) : (row.Count, row.Max);
        });

    private GameDataProvider? BuildProvider() {
        var docs = WithDb(db => db.GameDataDocuments.AsNoTracking()
            .ToDictionary(d => d.Id, d => d.Json, StringComparer.Ordinal));
        if (docs is null) return null;
        var missing = GameDataProvider.RequiredDocumentIds.Where(id => !docs.ContainsKey(id)).ToList();
        if (missing.Count > 0) {
            logger.LogWarning("game data provider unavailable, missing documents {Missing}",
                string.Join(", ", missing));
            return null;
        }

        try {
            return GameDataProvider.FromDocuments(docs);
        } catch (GameDataSchemaException ex) {
            logger.LogWarning(ex, "Game data documents failed schema validation; provider unavailable");
            return null;
        }
    }

    private T? WithDb<T>(Func<EggIncognitoDbContext, T> query) {
        try {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetService<EggIncognitoDbContext>();
            return db is null ? default : query(db);
        } catch {
            return default;
        }
    }
}
