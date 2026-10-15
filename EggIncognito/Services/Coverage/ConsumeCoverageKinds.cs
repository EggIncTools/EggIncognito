using EggSharp.GameData.Artifacts;

namespace EggIncognito.Services.Coverage;

public static class ConsumeCoverageKinds {
    public static bool Consumable(ArtifactKind kind) =>
        kind is ArtifactKind.Artifact or ArtifactKind.Stone or ArtifactKind.Ingredient;

    public static string DisplayName(ArtifactKind kind, string specName, string pluralName) =>
        kind == ArtifactKind.Stone ? specName.Replace('_', ' ') + "S" : pluralName;
}
