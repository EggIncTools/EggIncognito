namespace EggIncognito.Models.Events;

public sealed record EventBacktestSweep(
    IReadOnlyList<EventBacktestResult> Windows,
    IReadOnlyList<EventBacktestKindResult> Totals);
