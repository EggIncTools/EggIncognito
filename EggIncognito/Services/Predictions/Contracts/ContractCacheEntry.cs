using EggIncognito.Models.Calendar;
using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions.Contracts;

public sealed record ContractCacheEntry(
    IReadOnlyList<ContractReleaseSample> Samples,
    TunedModel<ContractStrategy, ContractModel> Tuned,
    TunedModel<ColleggtibleStrategy, ColleggtibleModel>? Colleggtibles,
    IReadOnlyList<ContractSlotPrediction> Slots,
    PredictionHorizons Horizons,
    double ComputedAt,
    IReadOnlyDictionary<int, IReadOnlyList<WatchEntry>> Watches,
    IReadOnlyDictionary<int, IReadOnlyList<WatchEntry>>? ColleggtibleWatches,
    NextContractsResponse? Next);
