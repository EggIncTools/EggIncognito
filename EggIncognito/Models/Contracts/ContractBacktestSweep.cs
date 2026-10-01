namespace EggIncognito.Models.Contracts;

public sealed record ContractBacktestSweep(
    IReadOnlyList<ContractBacktestResult> Windows,
    IReadOnlyList<ContractBacktestKindResult> Totals);
