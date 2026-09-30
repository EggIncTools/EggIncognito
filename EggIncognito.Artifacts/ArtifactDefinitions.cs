using System.Text.Json;
using System.Text.Json.Serialization;

namespace EggIncognito.Artifacts;

public static class ArtifactDefinitions {
    private const string ResourceName = "artifact-definitions.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<Snapshot> Loaded = new(Load);

    public static IReadOnlyList<ArtifactFamily> All => Loaded.Value.Rows;

    public static IReadOnlyDictionary<int, string> Labels => Loaded.Value.LabelMap;

    public static string BinaryVersion => Loaded.Value.Version;

    public static ArtifactFamily? Find(string name) =>
        Loaded.Value.ByName.TryGetValue(name, out var f) ? f : null;

    public static ArtifactFamily? Find(int afxId) =>
        Loaded.Value.ById.TryGetValue(afxId, out var f) ? f : null;

    public static ArtifactKind? Kind(string name) => Find(name)?.Kind;

    public static string? TierName(string name, int level) =>
        Find(name)?.Tiers.FirstOrDefault(t => t.Level == level)?.Name;

    public static string? DimensionLabel(int dimension) => Labels.TryGetValue(dimension, out string? l) ? l : null;

    public static string EffectTemplate => Loaded.Value.Template;

    public static ArtifactEffect? Effect(string name, int level, int rarity) =>
        Find(name)?.Tiers.FirstOrDefault(t => t.Level == level)?.Effects.FirstOrDefault(e => e.Rarity == rarity);

    public static string? EffectMarkup(string name, int level, int rarity, string currency = ArtifactEffects.DefaultCurrency) {
        if (Find(name) is not { } family || Effect(name, level, rarity) is not { } effect) return null;
        string template = effect.Template.Length > 0 ? effect.Template : EffectTemplate;
        return ArtifactEffects.Render(template, family.Dimension, effect.Magnitude, DimensionLabel, currency);
    }

    public static string? EffectText(string name, int level, int rarity, string currency = ArtifactEffects.DefaultCurrency) =>
        EffectMarkup(name, level, rarity, currency) is { } markup ? ArtifactEffects.Plain(markup) : null;

    public static string? FragmentFamily(string fragmentName) {
        if (Find(fragmentName) is not { } fragment) return null;
        return All.FirstOrDefault(f => f.Kind == ArtifactKind.Stone && f.Tiers.Count > 0
                                       && f.Tiers[0].Recipe.Any(r => r.AfxId == fragment.AfxId))?.Name;
    }

    public static (IReadOnlyList<ArtifactFamily> Families, IReadOnlyDictionary<int, string> Labels,
        string BinaryVersion, string EffectTemplate) Parse(string json) {
        var file = JsonSerializer.Deserialize<DefinitionsFile>(json, Json)
                   ?? throw new InvalidOperationException("artifact definitions are empty");
        return (file.Rows ?? [], file.LabelMap ?? new Dictionary<int, string>(), file.Version ?? "",
            file.Template ?? "");
    }

    public static string Fingerprint(IReadOnlyList<ArtifactFamily> families, IReadOnlyDictionary<int, string> labels,
        string effectTemplate = "") =>
        JsonSerializer.Serialize(new { families, labels = labels.OrderBy(k => k.Key), effectTemplate }, Json);

    private static Snapshot Load() {
        using var stream = typeof(ArtifactDefinitions).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"embedded {ResourceName} missing");
        using var reader = new StreamReader(stream);
        var (families, labels, version, template) = Parse(reader.ReadToEnd());
        return new Snapshot(families, labels, version, template,
            families.ToDictionary(f => f.Name, StringComparer.Ordinal),
            families.ToDictionary(f => f.AfxId));
    }

    private sealed record DefinitionsFile(
        [property: JsonPropertyName("families")] IReadOnlyList<ArtifactFamily>? Rows,
        [property: JsonPropertyName("dimensionLabels")] IReadOnlyDictionary<int, string>? LabelMap,
        [property: JsonPropertyName("effectTemplate")] string? Template,
        [property: JsonPropertyName("binaryVersion")] string? Version);

    private sealed record Snapshot(
        IReadOnlyList<ArtifactFamily> Rows,
        IReadOnlyDictionary<int, string> LabelMap,
        string Version,
        string Template,
        IReadOnlyDictionary<string, ArtifactFamily> ByName,
        IReadOnlyDictionary<int, ArtifactFamily> ById);
}
