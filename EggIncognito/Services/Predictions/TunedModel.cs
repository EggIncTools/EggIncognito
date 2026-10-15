namespace EggIncognito.Services.Predictions;

public sealed record TunedModel<TStrategy, TModel>(
    TStrategy Strategy,
    TModel Model,
    IReadOnlyList<StrategyScore> Scoreboard,
    double RunnerUpDelta);
