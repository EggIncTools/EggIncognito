using EggSharp.GameData.Artifacts;

namespace EggIncognito.Services.Coverage;

public static class ConsumeCoverageKinds {
    public static bool Consumable(ArtifactKind kind) =>
        kind is ArtifactKind.Artifact or ArtifactKind.Stone or ArtifactKind.Ingredient;
}
