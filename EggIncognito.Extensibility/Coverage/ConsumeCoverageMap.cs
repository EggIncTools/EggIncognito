namespace EggIncognito.Models.Coverage;

public sealed record ConsumeCoverageMap(
    double Validity,
    int CellsInScope,
    int CellsComplete,
    int Items,
    int Observations,
    IReadOnlyList<CoverageFamily> Families);
