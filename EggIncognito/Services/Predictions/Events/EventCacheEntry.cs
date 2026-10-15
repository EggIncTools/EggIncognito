using EggIncognito.Models.Events;

namespace EggIncognito.Services.Predictions.Events;

public sealed record EventCacheEntry(
    IReadOnlyList<EventRow> Rows,
    TunedModel<EventStrategy, EventModel> Tuned,
    IReadOnlyList<EventPrediction> Projection);
