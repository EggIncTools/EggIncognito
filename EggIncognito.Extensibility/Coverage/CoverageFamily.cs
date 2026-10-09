namespace EggIncognito.Models.Coverage;

public sealed record CoverageFamily(
    string SpecName,
    string PluralName,
    double Validity,
    int CellsInScope,
    int CellsComplete,
    int Items,
    int Observations,
    IReadOnlyList<CoverageTier> Tiers);
