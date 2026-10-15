using EggSharp.GameData.Artifacts;

namespace EggIncognito.Models.Coverage;

public sealed record CoverageFamily(
    string SpecName,
    string DisplayName,
    ArtifactKind Kind,
    double Validity,
    int CellsInScope,
    int CellsComplete,
    int Items,
    int Observations,
    IReadOnlyList<CoverageTier> Tiers);
