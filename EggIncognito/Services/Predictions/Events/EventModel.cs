namespace EggIncognito.Services.Predictions.Events;

public sealed record EventModel(double TrainedAt, int WindowDays, IReadOnlyList<IEventRule> Rules);
