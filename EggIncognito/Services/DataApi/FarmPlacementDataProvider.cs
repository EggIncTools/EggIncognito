using EggSharp.GameData;
using EggSharp.GameData.Farm;

namespace EggIncognito.Services.DataApi;

public sealed class FarmPlacementDataProvider(GameDataStore store) {
    public const string MissingDocument =
        "the farm-placement game data document has not been imported; rebuild game data on a machine that has "
        + "the game binary";

    private readonly Lock _gate = new();
    private string? _json;
    private FarmPlacementData? _cached;

    public Task<(FarmPlacementData? Data, string? Diagnostics)> GetAsync(CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Resolve());
    }

    private (FarmPlacementData? Data, string? Diagnostics) Resolve() {
        string? json = store.Doc(FarmPlacementCatalog.DocumentId);
        if (string.IsNullOrEmpty(json)) return (null, MissingDocument);

        lock (_gate) {
            if (_cached is not null && string.Equals(_json, json, StringComparison.Ordinal)) return (_cached, null);
            try {
                var parsed = FarmPlacementCatalog.Parse(json);
                _json = json;
                _cached = parsed;
                return (parsed, null);
            } catch (GameDataSchemaException ex) {
                _json = null;
                _cached = null;
                return (null, $"the farm-placement document failed schema validation: {ex.Message}");
            }
        }
    }
}
