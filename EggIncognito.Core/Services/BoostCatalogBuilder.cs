using System.Text.Json;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Core.Services;

public static class BoostCatalogBuilder {
    public static readonly JsonSerializerOptions CamelJson = JsonPresets.CamelIndentedRelaxed;

    public static BuildResult Build(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms,
        IReadOnlyList<MachoSections.Section> sections, string configJson, string binaryVersion) {
        var extracted = BoostCatalogExtractor.ExtractWith(bin, syms, sections);
        if (!extracted.Ok || extracted.Entries.Count == 0) {
            throw new InvalidOperationException(extracted.Diagnostics is { Length: > 0 } d
                ? d
                : $"boost extraction produced no entries ({BoostCatalogExtractor.InitSymbol} unresolved?)");
        }

        var entries = extracted.Entries;
        var costs = BoostCostExtractor.FromConfigJson(configJson);

        var rows = new List<BoostCatalogBuildRow>(entries.Count);
        var missing = new List<string>();

        foreach (var e in entries) {
            string bare = e.Id.EndsWith("_v2", StringComparison.Ordinal) ? e.Id[..^3] : e.Id;
            string iconAsset = "b_icon_" + bare;

            if (costs.TryGetValue(e.Id, out var cost)) {
                rows.Add(new BoostCatalogBuildRow(e.Id, e.DisplayName, e.Description, cost.Price, cost.TokenPrice,
                    cost.SeRequired, iconAsset));
            } else {
                missing.Add(e.Id);
                rows.Add(new BoostCatalogBuildRow(e.Id, e.DisplayName, e.Description, null, null, null, iconAsset));
            }
        }

        var provenance = new Dictionary<string, ProvenanceSource>(StringComparer.Ordinal) {
            ["identity"] = new("binary", "boostmanager"),
            ["description"] = new("binary", "boostmanager", "decoded"),
            ["cost"] = new("config", "ei/get_config"),
            ["iconAsset"] = new("derived")
        };
        var file = new BoostCatalogBuildFile(rows, binaryVersion, provenance);
        return new BuildResult(file, missing);
    }

    public static string Serialize(BoostCatalogBuildFile file) => JsonSerializer.Serialize(file, CamelJson);

    public sealed record BoostCatalogBuildRow(
        string Id,
        string? DisplayName,
        string? Description,
        int? Price,
        int? TokenPrice,
        double? SeRequired,
        string IconAsset);

    public sealed record ProvenanceSource(string Origin, string? Locator = null, string? Method = null);

    public sealed record BoostCatalogBuildFile(
        IReadOnlyList<BoostCatalogBuildRow> Boosts,
        string BinaryVersion,
        IReadOnlyDictionary<string, ProvenanceSource> Provenance);

    public readonly record struct BuildResult(BoostCatalogBuildFile File, IReadOnlyList<string> MissingCosts);
}
