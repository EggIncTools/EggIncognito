namespace EggIncognito.Models.Events;

public sealed record EventModelResponse(
    double TrainedAt,
    int WindowDays,
    IReadOnlyList<EventRuleSummary> Rules,
    EventBacktestSweep Sweep);
