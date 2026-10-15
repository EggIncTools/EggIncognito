namespace EggIncognito.Services.Predictions.Events;

public sealed record EventCacheEntry(IReadOnlyList<EventRow> Rows, TunedModel<EventStrategy, EventModel> Tuned);
