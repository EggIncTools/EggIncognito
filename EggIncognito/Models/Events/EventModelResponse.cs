using EggIncognito.Models.Predictions;

namespace EggIncognito.Models.Events;

public sealed record EventModelResponse(
    double TrainedAt,
    int WindowDays,
    IReadOnlyList<EventRuleSummary> Rules,
    TunedStrategySummary Strategy);
