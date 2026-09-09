namespace EggIncognito.Models.Tools;

public sealed record BoostCostsResult(
    int Count,
    IReadOnlyList<BoostCostRow>? Costs = null,
    string? Error = null);
