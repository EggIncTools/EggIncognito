using EggIncognito.Services.Predictions;

namespace EggIncognito.Tests.Predictions;

public class StrategyTunerTests {
    private sealed record FakeStrategy(string Label, double Probability) : IStrategy;

    private sealed record FakeModel(FakeStrategy Strategy);

    private sealed class FakeDomain : IPredictionDomain<int, FakeStrategy, FakeModel> {
        public string Name => "fake";

        public IReadOnlyList<FakeStrategy> Strategies => [new FakeStrategy("weak", 0.3), new FakeStrategy("strong", 0.9)];

        public IReadOnlyList<PredictionSlot> Slots(IReadOnlyList<int> samples, double from, double to) =>
            [new PredictionSlot(from, "k")];

        public FakeModel Train(IReadOnlyList<int> samples, double asOf, FakeStrategy strategy) => new(strategy);

        public IReadOnlyList<PredictionCandidate> Odds(FakeModel model, PredictionSlot slot) =>
            [
                new PredictionCandidate("actual", model.Strategy.Probability),
                new PredictionCandidate("other", 1 - model.Strategy.Probability)
            ];

        public IReadOnlySet<string> Actual(IReadOnlyList<int> samples, PredictionSlot slot) =>
            new HashSet<string>(StringComparer.Ordinal) { "actual" };
    }

    [Fact]
    public void Fit_TwoStrategies_PicksTheLowerLogLossStrategy() {
        var domain = new FakeDomain();

        var tuned = StrategyTuner.Fit(domain, [], 1000, lagSeconds: 0);

        Assert.Equal("strong", tuned.Strategy.Label);
        Assert.True(tuned.RunnerUpDelta > 0);
        Assert.Equal(2, tuned.Scoreboard.Count);
    }
}
