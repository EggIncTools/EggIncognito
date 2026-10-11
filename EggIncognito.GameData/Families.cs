namespace EggIncognito.GameData;

public static class Families {
    public const string Boost = "boost";
    public const string Research = "research";
    public const string Hab = "hab";
    public const string Artifact = "artifact";
}

public abstract class EffectFamily : IEffectFamily {
    private readonly Dictionary<string, Effect> _byId;

    protected EffectFamily(string key, EffectDataFile file, EffectSchema? metaSchema) {
        Key = key;
        MetaSchema = metaSchema;
        Effects = EffectDataLoader.ToEffects(key, file, metaSchema);
        BinaryVersion = file.BinaryVersion;
        Provenance = file.Provenance ?? GameData.Provenance.Empty;
        _byId = Effects.ToDictionary(e => e.Id, StringComparer.Ordinal);
    }

    public string BinaryVersion { get; }

    public string Key { get; }
    public EffectSchema? MetaSchema { get; }
    public IReadOnlyList<Effect> Effects { get; }
    public IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }

    public Effect? Find(string id) => _byId.GetValueOrDefault(id);
}

public sealed class BoostFamily(EffectDataFile file) : EffectFamily(Families.Boost, file, MetaSchemaDef) {
    private static readonly EffectSchema MetaSchemaDef = new([
        new EffectField("kind", EffectFieldType.String),
        new EffectField("durationSeconds", EffectFieldType.Int),
        new EffectField("multiplier", EffectFieldType.Double, false),
        new EffectField("appliesTo", EffectFieldType.String, false),
        new EffectField("slotCap", EffectFieldType.Int, false),
        new EffectField("price", EffectFieldType.Int, false),
        new EffectField("tokenPrice", EffectFieldType.Int, false),
        new EffectField("seRequired", EffectFieldType.Double, false),
        new EffectField("iconAsset", EffectFieldType.String, false)
    ]);
}

public sealed class ResearchFamily(EffectDataFile file) : EffectFamily(Families.Research, file, MetaSchemaDef) {
    private static readonly EffectSchema MetaSchemaDef = new([
        new EffectField("name", EffectFieldType.String, false),
        new EffectField("epic", EffectFieldType.Bool),
        new EffectField("description", EffectFieldType.String, false),
        new EffectField("help", EffectFieldType.String, false),
        new EffectField("dimension", EffectFieldType.String, false),
        new EffectField("tier", EffectFieldType.Int, false),
        new EffectField("research", EffectFieldType.String, false)
    ]);
}

public sealed class HabFamily(EffectDataFile file) : EffectFamily(Families.Hab, file, MetaSchemaDef) {
    private static readonly EffectSchema MetaSchemaDef = new([
        new EffectField("habId", EffectFieldType.Int),
        new EffectField("name", EffectFieldType.String, false)
    ]);
}

public sealed class ArtifactFamily(EffectDataFile file) : EffectFamily(Families.Artifact, file, MetaSchemaDef) {
    private static readonly EffectSchema MetaSchemaDef = new([
        new EffectField("boost", EffectFieldType.String),
        new EffectField("tier", EffectFieldType.Int, false),
        new EffectField("rarity", EffectFieldType.Int, false),
        new EffectField("stoneCap", EffectFieldType.Int, false),
        new EffectField("iconAsset", EffectFieldType.String, false)
    ]);
}
