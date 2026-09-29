namespace EggIncognito.Artifacts;

public sealed record ArtifactFamily(
    string Name,
    int AfxId,
    string BinaryId,
    string PluralName,
    ArtifactKind Kind,
    int Dimension,
    int Order,
    IReadOnlyList<ArtifactTier> Tiers);
