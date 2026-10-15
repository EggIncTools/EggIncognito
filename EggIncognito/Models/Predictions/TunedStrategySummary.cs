using EggIncognito.Services.Predictions;

namespace EggIncognito.Models.Predictions;

public sealed record TunedStrategySummary(
    string Label,
    IReadOnlyList<StrategyScore> Scoreboard,
    WalkForwardResult WalkForward,
    double RunnerUpDelta);
