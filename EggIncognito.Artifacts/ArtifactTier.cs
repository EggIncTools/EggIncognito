namespace EggIncognito.Artifacts;

public sealed record ArtifactTier(int Level, string Name, int RarityCount, IReadOnlyList<ArtifactIngredient> Recipe);
