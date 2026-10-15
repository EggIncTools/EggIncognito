using EggIncognito.Services.Predictions.Training;

namespace EggIncognito.Services.Predictions;

public static class StrategyTuner {
    public static TunedModel<TStrategy, TModel> Fit<TSample, TStrategy, TModel>(
        IPredictionDomain<TSample, TStrategy, TModel> domain,
        IReadOnlyList<TSample> samples,
        double asOf,
        double lagSeconds = WalkForward.DefaultLagSeconds,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken ct = default) where TStrategy : notnull, IStrategy {
        double from = asOf - WalkForward.DefaultRangeSeconds;
        var results = new WalkForwardResult[domain.Strategies.Count];
        int strategyCount = results.Length;
        var options = new ParallelOptions { CancellationToken = ct };
        Parallel.For(0, results.Length, options, i => {
            progress?.Report(new TrainingProgress(i, strategyCount, 0, 0));
            var relay = progress is null ? null : new StrategyProgressRelay(progress, i, strategyCount);
            results[i] = WalkForward.Run(domain, samples, domain.Strategies[i], lagSeconds, from, asOf, relay, ct);
        });
        var scored = domain.Strategies
            .Select((strategy, index) => (Strategy: strategy, Index: index, Result: results[index]))
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

    private sealed class StrategyProgressRelay(IProgress<TrainingProgress> inner, int strategyIndex, int strategyCount)
        : IProgress<TrainingProgress> {
        public void Report(TrainingProgress value) => inner.Report(value with { StrategyIndex = strategyIndex, StrategyCount = strategyCount });
    }
}
