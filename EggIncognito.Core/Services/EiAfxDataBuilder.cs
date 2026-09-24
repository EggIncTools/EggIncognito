using System.Collections.Frozen;
using System.Text;
using Ei;

namespace EggIncognito.Core.Services;

public sealed record ProvenanceSource(string Origin, string? Locator = null, string? Method = null);

public sealed record EiAfxData(
    IReadOnlyList<EiAfxFamily> ArtifactFamilies,
    IReadOnlyDictionary<string, ProvenanceSource> Provenance);

public sealed record EiAfxFamily(
    string Id,
    string SpecName,
    int AfxId,
    int AfxType,
    string Type,
    int SortKey,
    IReadOnlyList<int> ChildAfxIds,
    IReadOnlyList<EiAfxTier> Tiers);

public sealed record EiAfxTier(
    string Id,
    string SpecName,
    int AfxId,
    int AfxLevel,
    int TierNumber,
    string? IconFilename,
    double Quality,
    double Value,
    bool Craftable,
    IReadOnlyList<double> BaseCraftingPrices,
    bool HasRarities,
    IReadOnlyList<int> PossibleAfxRarities);

public static class EiAfxDataBuilder {
    private static readonly FrozenDictionary<string, ProvenanceSource> DefaultProvenance =
        new Dictionary<string, ProvenanceSource>(StringComparer.Ordinal) {
            ["families"] = new("fixture", "ei_afx/config", "captured"),
            ["icons"] = new("config", "ei/get_config")
        }.ToFrozenDictionary();

    private static readonly FrozenSet<string> IngredientNames = new[] {
        "EXTRATERRESTRIAL_ALUMINUM", "ANCIENT_TUNGSTEN", "SPACE_ROCKS", "ALIEN_WOOD",
        "GOLD_METEORITE", "TAU_CETI_GEODE", "CENTAURIAN_STEEL", "ERIDANI_FEATHER",
        "DRONE_PARTS", "CELESTIAL_BRONZE", "LALANDE_HIDE", "SOLAR_TITANIUM"
    }.ToFrozenSet();

    public static EiAfxData Build(
        ArtifactsConfigurationResponse cfg,
        IReadOnlyDictionary<string, string> icons) {
        var families = cfg.ArtifactParameters
            .Where(p => p.Spec is not null)
            .GroupBy(p => p.Spec.Name)
            .OrderBy(g => (int)g.Key)
            .Select(g => BuildFamily(g.Key, [.. g], icons))
            .ToList();

        return new EiAfxData(families, DefaultProvenance);
    }

    public static EiAfxData BuildFromJson(
        string configJson,
        IReadOnlyDictionary<string, string> icons) =>
        Build(ArtifactsConfigurationResponse.Parser.ParseJson(configJson), icons);

    private static EiAfxFamily BuildFamily(
        ArtifactSpec.Types.Name name,
        List<ArtifactsConfigurationResponse.Types.ArtifactParameters> rows,
        IReadOnlyDictionary<string, string> icons) {
        int afxId = (int)name;
        string screaming = Screaming(name);
        string id = screaming.ToLowerInvariant().Replace('_', '-');
        (int afxType, string typeName) = Classify(screaming);

        var tiers = rows
            .GroupBy(r => r.Spec.Level)
            .OrderBy(g => (int)g.Key)
            .Select(g => BuildTier(screaming, id, afxId, g.Key, [.. g], icons))
            .ToList();

        return new EiAfxFamily(id, screaming, afxId, afxType, typeName, afxId, [afxId], tiers);
    }

    private static EiAfxTier BuildTier(
        string screaming, string familyId, int afxId,
        ArtifactSpec.Types.Level level,
        List<ArtifactsConfigurationResponse.Types.ArtifactParameters> rows,
        IReadOnlyDictionary<string, string> icons) {
        int levelInt = (int)level;
        int tierNumber = levelInt + 1;

        var rarities = rows.Select(r => (int)r.Spec.Rarity).Distinct().Order().ToList();
        var byRarity = rows.ToDictionary(r => (int)r.Spec.Rarity, r => r);
        var baseRow = byRarity.TryGetValue(0, out var common) ? common : rows[0];
        var prices = rarities.Select(r => byRarity[r].CraftingPrice).ToList();

        string stem = $"afx_{screaming.ToLowerInvariant()}_{tierNumber}";
        string? icon = icons.GetValueOrDefault(stem);

        return new EiAfxTier(
            $"{familyId}-{tierNumber}",
            screaming,
            afxId,
            levelInt,
            tierNumber,
            icon,
            baseRow.BaseQuality,
            baseRow.Value,
            baseRow.CraftingPrice > 0,
            prices,
            rarities.Count > 1,
            rarities);
    }

    private static (int AfxType, string Name) Classify(string screaming) {
        if (screaming.EndsWith("_STONE_FRAGMENT", StringComparison.Ordinal)) return (3, "Stone_Ingredient");
        if (screaming.EndsWith("_STONE", StringComparison.Ordinal)) return (1, "Stone");
        if (IngredientNames.Contains(screaming)) return (2, "Ingredient");
        return (0, "Artifact");
    }

    private static string Screaming(ArtifactSpec.Types.Name name) {
        string pascal = name.ToString();
        var sb = new StringBuilder(pascal.Length + 8);
        for (int i = 0; i < pascal.Length; i++) {
            char c = pascal[i];
            if (i > 0 && char.IsUpper(c)) sb.Append('_');
            sb.Append(char.ToUpperInvariant(c));
        }

        return sb.ToString();
    }
}
