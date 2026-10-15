namespace EggIncognito.Services.Predictions.Contracts;

public sealed record ContractCacheEntry(
    IReadOnlyList<ContractReleaseSample> Samples,
    TunedModel<ContractStrategy, ContractModel> Tuned,
    TunedModel<ColleggtibleStrategy, ColleggtibleModel> Colleggtibles);
