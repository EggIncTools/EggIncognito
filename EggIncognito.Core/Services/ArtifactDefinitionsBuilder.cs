using System.Globalization;
using System.Text.Json;
using EggIncognito.Core.Services.ProtoExtract;
using Ei;

namespace EggIncognito.Core.Services;

public static class ArtifactDefinitionsBuilder {
    public static string Build(ArtifactTableExtractor.Result table, DimensionLabelExtractor.Result labels,
        string binaryVersion) {
        if (!table.Ok) throw new InvalidOperationException(table.Diagnostics);
        if (!labels.Ok) throw new InvalidOperationException(labels.Diagnostics);

        var families = table.Families
            .OrderBy(f => f.Order)
            .Select(f => new FamilyRow(
                ProtoEnumNames.SpecName((ArtifactSpec.Types.Name)f.AfxId),
                f.AfxId,
                f.BinaryId,
                f.PluralName,
                f.Kind,
                f.Dimension,
                f.Order,
                f.Description,
                [.. f.Tiers.OrderBy(t => t.Level).Select(t => new TierRow(t.Level, t.Name, t.RarityCount,
                    [.. t.Recipe.Select(r => new IngredientRow(r.AfxId, r.Level, r.Count))],
                    [.. t.Effects.Select(e => new EffectRow(e.Rarity, e.Magnitude, e.Template))]))]))
            .ToList();

        var dimensionLabels = labels.Labels
            .OrderBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key.ToString(CultureInfo.InvariantCulture), kv => kv.Value, StringComparer.Ordinal);

        var provenance = new Dictionary<string, ProvenanceSource>(StringComparer.Ordinal) {
            ["identity"] = new("binary", "artifactdata", "decoded"),
            ["effects"] = new("binary", "artifactdata / ArtifactSpec::effectDescription", "decoded"),
            ["labels"] = new("binary", "GameDimensions::name_str", "decoded")
        };

        return JsonSerializer.Serialize(
            new DefinitionsFile(families, dimensionLabels, table.EffectTemplate, binaryVersion, provenance),
            JsonPresets.CamelIndentedRelaxed);
    }

    private sealed record IngredientRow(int AfxId, int Level, int Count);

    private sealed record EffectRow(int Rarity, double Magnitude, string Template);

    private sealed record TierRow(int Level, string Name, int RarityCount, IReadOnlyList<IngredientRow> Recipe,
        IReadOnlyList<EffectRow> Effects);

    private sealed record FamilyRow(string Name, int AfxId, string BinaryId, string PluralName, int Kind,
        int Dimension, int Order, string Description, IReadOnlyList<TierRow> Tiers);

    private sealed record DefinitionsFile(IReadOnlyList<FamilyRow> Families,
        IReadOnlyDictionary<string, string> DimensionLabels, string EffectTemplate, string BinaryVersion,
        IReadOnlyDictionary<string, ProvenanceSource> Provenance);
}
