using System.Text.Json;
using Ei;

namespace EggIncognito.Core.Services;

public static class ArtifactCatalogBuilder {
    public static readonly JsonSerializerOptions CamelJson = JsonPresets.CamelIndentedRelaxed;

    public static BuildResult Build(ArtifactsConfigurationResponse cfg, string gameVersion) {
        var rows = new List<ArtifactCatalogBuildRow>();
        var skipped = new List<string>();

        foreach (var p in cfg.ArtifactParameters) {
            if (p.Spec is null) {
                skipped.Add("row without spec");
                continue;
            }

            int afxId = (int)p.Spec.Name;
            int afxLevel = (int)p.Spec.Level;
            int afxRarity = (int)p.Spec.Rarity;
            string specName = ProtoEnumNames.SpecName(p.Spec.Name);
            string level = ProtoEnumNames.LevelName(p.Spec.Level);
            string rarity = ProtoEnumNames.RarityName(p.Spec.Rarity);
            int tierNumber = afxLevel + 1;

            rows.Add(new ArtifactCatalogBuildRow(
                $"{Slug(specName)}-{tierNumber}-{rarity.ToLowerInvariant()}",
                specName,
                level,
                rarity,
                afxId,
                afxLevel,
                afxRarity,
                tierNumber,
                p.BaseQuality,
                p.OddsMultiplier,
                p.Value,
                p.CraftingPrice,
                p.CraftingPriceLow,
                p.CraftingPriceCurve,
                p.CraftingPriceDomain,
                p.CraftingXp));
        }

        if (rows.Count == 0) throw new InvalidOperationException("ei_afx/config carried no artifact parameters");

        var levels = cfg.CraftingLevelInfos
            .Select((info, i) => new ArtifactCraftingLevelBuildRow(i + 1, info.XpRequired, info.RarityMult))
            .ToList();

        var source = new ProvenanceSource("config", "ei_afx/config", "decoded");
        var provenance = new Dictionary<string, ProvenanceSource>(StringComparer.Ordinal) {
            ["parameters"] = source,
            ["craftingLevels"] = source
        };

        return new BuildResult(new ArtifactCatalogBuildFile(rows, levels, gameVersion, provenance), skipped);
    }

    public static BuildResult BuildFromJson(string configJson, string gameVersion) =>
        Build(ArtifactsConfigurationResponse.Parser.ParseJson(configJson), gameVersion);

    public static string Serialize(ArtifactCatalogBuildFile file) => JsonSerializer.Serialize(file, CamelJson);

    private static string Slug(string screaming) => screaming.ToLowerInvariant().Replace('_', '-');

    public sealed record ArtifactCatalogBuildRow(
        string Id,
        string SpecName,
        string Level,
        string Rarity,
        int AfxId,
        int AfxLevel,
        int AfxRarity,
        int TierNumber,
        double BaseQuality,
        double OddsMultiplier,
        double Value,
        double CraftingPrice,
        double CraftingPriceLow,
        double CraftingPriceCurve,
        uint CraftingPriceDomain,
        ulong CraftingXp);

    public sealed record ArtifactCraftingLevelBuildRow(int Level, double XpRequired, double RarityMult);

    public sealed record ArtifactCatalogBuildFile(
        IReadOnlyList<ArtifactCatalogBuildRow> Artifacts,
        IReadOnlyList<ArtifactCraftingLevelBuildRow> CraftingLevels,
        string BinaryVersion,
        IReadOnlyDictionary<string, ProvenanceSource> Provenance);

    public readonly record struct BuildResult(ArtifactCatalogBuildFile File, IReadOnlyList<string> Skipped);
}
