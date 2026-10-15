using EggIncognito.Models.Calendar;
using EggIncognito.Models.Events;

namespace EggIncognito.Services.Predictions.Events;

public sealed record EventCacheEntry(
    IReadOnlyList<EventRow> Rows,
    TunedModel<EventStrategy, EventModel> Tuned,
    IReadOnlyList<EventPrediction> Projection,
    PredictionHorizons Horizons,
    double ComputedAt,
    IReadOnlyDictionary<int, IReadOnlyList<WatchEntry>> Watches);
