using EggSharp.GameData.Artifacts;

namespace EggIncognito.Models.Coverage;

public sealed record CoverageFamilyInfo(string SpecName, string DisplayName, ArtifactKind Kind, int Order,
    IReadOnlyList<string> TierNames);
