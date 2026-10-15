namespace EggIncognito.Services.Predictions;

public static class StrategyTuner {
    public static TunedModel<TStrategy, TModel> Fit<TSample, TStrategy, TModel>(
        IPredictionDomain<TSample, TStrategy, TModel> domain,
        IReadOnlyList<TSample> samples,
        double asOf,
        double lagSeconds = WalkForward.DefaultLagSeconds) where TStrategy : notnull, IStrategy {
        double from = asOf - WalkForward.DefaultRangeSeconds;
        var scored = domain.Strategies
            .Select((strategy, index) => (
                Strategy: strategy,
                Index: index,
                Result: WalkForward.Run(domain, samples, strategy, lagSeconds, from, asOf)))
            .OrderBy(x => x.Result.Total.LogLoss)
            .ThenByDescending(x => x.Result.Total.Top1)
            .ThenBy(x => x.Index)
            .ToList();

        var scoreboard = scored.Select(x => new StrategyScore(x.Strategy.Label, x.Result)).ToList();
        var winner = scored[0];
        double runnerUpDelta = scored.Count > 1 ? scored[1].Result.Total.LogLoss - winner.Result.Total.LogLoss : 0;
        var model = domain.Train(samples, asOf, winner.Strategy);

        return new TunedModel<TStrategy, TModel>(winner.Strategy, model, scoreboard, runnerUpDelta);
    }
}
