namespace EggIncognito.Models.Contracts;

public sealed record ContractBacktestResult(
    double AsOf,
    int HorizonSlots,
    IReadOnlyList<ContractBacktestKindResult> Kinds,
    int ActualUncovered);
