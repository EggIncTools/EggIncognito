namespace EggIncognito.Services.Predictions;

public interface IPredictionDomain<TSample, TStrategy, TModel> where TStrategy : notnull, IStrategy {
    string Name { get; }

    IReadOnlyList<TStrategy> Strategies { get; }

    IReadOnlyList<PredictionSlot> Slots(IReadOnlyList<TSample> samples, double from, double to);

    TModel Train(IReadOnlyList<TSample> samples, double asOf, TStrategy strategy);

    IReadOnlyList<PredictionCandidate> Odds(TModel model, PredictionSlot slot);

    IReadOnlySet<string> Actual(IReadOnlyList<TSample> samples, PredictionSlot slot);
}
