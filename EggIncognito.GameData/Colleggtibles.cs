using System.Collections.Frozen;

namespace EggIncognito.GameData;

public sealed record ColleggtibleEgg(string Identifier, int Dimension, IReadOnlyList<double> TierValues);

public interface IColleggtibleCatalog {
    IReadOnlyList<ColleggtibleEgg> Eggs { get; }
    IReadOnlyDictionary<string, string> ContractEggMap { get; }
    string GameVersion { get; }
    IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }
    ColleggtibleEgg? Find(string identifier);
}

public sealed class ColleggtibleCatalog : GameDataCatalog<ColleggtibleEgg, string>, IColleggtibleCatalog {
    public static readonly FrozenDictionary<string, int> DimensionCodes =
        new Dictionary<string, int> {
            ["INVALID"] = 0,
            ["EARNINGS"] = 1,
            ["AWAY_EARNINGS"] = 2,
            ["INTERNAL_HATCHERY_RATE"] = 3,
            ["EGG_LAYING_RATE"] = 4,
            ["SHIPPING_CAPACITY"] = 5,
            ["HAB_CAPACITY"] = 6,
            ["VEHICLE_COST"] = 7,
            ["HAB_COST"] = 8,
            ["RESEARCH_COST"] = 9
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private ColleggtibleCatalog(IReadOnlyList<ColleggtibleEgg> eggs, IReadOnlyDictionary<string, string> map,
        string gameVersion, IReadOnlyDictionary<string, ProvenanceSource> provenance)
        : base(eggs, gameVersion, provenance, e => e.Identifier, StringComparer.Ordinal) {
        ContractEggMap = map;
    }

    public IReadOnlyList<ColleggtibleEgg> Eggs => Entries;
    public IReadOnlyDictionary<string, string> ContractEggMap { get; }
    public string GameVersion => Version;

    public ColleggtibleEgg? Find(string identifier) => FindByKey(identifier);

    public static ColleggtibleCatalog Parse(string json) {
        var file = GameDataJson.Deserialize<ColleggtibleDataFile>(json, "Colleggtible catalog");
        if (file.Eggs is null) throw new GameDataSchemaException("Colleggtible catalog missing eggs.");
        var eggs = file.Eggs.Select(ToEgg).ToArray();
        var map = file.ContractEggMap ?? new Dictionary<string, string>(0);
        return new ColleggtibleCatalog(eggs, map, file.GameVersion ?? "", file.Provenance ?? GameData.Provenance.Empty);
    }

    private static ColleggtibleEgg ToEgg(ColleggtibleEggRow row) {
        if (string.IsNullOrEmpty(row.Identifier))
            throw new GameDataSchemaException("Colleggtible row missing identifier.");
        return !DimensionCodes.TryGetValue(row.Dimension ?? "", out int code)
            ? throw new GameDataSchemaException(
                $"Colleggtible '{row.Identifier}' has unknown dimension '{row.Dimension}'.")
            : row.TierValues is not { Count: 4 }
                ? throw new GameDataSchemaException($"Colleggtible '{row.Identifier}' must have exactly 4 tierValues.")
                : new ColleggtibleEgg(row.Identifier, code, row.TierValues);
    }
}

public sealed record ColleggtibleEggRow(string? Identifier, string? Dimension, IReadOnlyList<double>? TierValues);

public sealed record ColleggtibleDataFile(
    string? GameVersion,
    IReadOnlyDictionary<string, ProvenanceSource>? Provenance,
    IReadOnlyList<ColleggtibleEggRow> Eggs,
    IReadOnlyDictionary<string, string>? ContractEggMap);
