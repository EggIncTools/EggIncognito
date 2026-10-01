namespace EggIncognito.Models.Contracts;

public sealed record ContractModelResponse(
    double TrainedAt,
    IReadOnlyList<ContractGridSummary> Grid,
    IReadOnlyList<ContractPoolSummary> Pools,
    ContractBacktestSweep Sweep);
