using EggIncognito.Services.Predictions;
using EggIncognito.Services.Predictions.Training;

namespace EggIncognito.Tests.Predictions;

public class WalkForwardTests {
    private sealed class DelegateProgress<T>(Action<T> report) : IProgress<T> {
        public void Report(T value) => report(value);
    }

    private sealed class ManySlotsDomain : IPredictionDomain<int, FakeStrategy, FakeModel> {
        public string Name => "many";

        public IReadOnlyList<FakeStrategy> Strategies => [new FakeStrategy("x")];

        public IReadOnlyList<PredictionSlot> Slots(IReadOnlyList<int> samples, double from, double to) =>
            [.. Enumerable.Range(0, 25).Select(i => new PredictionSlot(i, "k"))];

        public FakeModel Train(IReadOnlyList<int> samples, double asOf, FakeStrategy strategy) => new(asOf);

        public IReadOnlyList<PredictionCandidate> Odds(FakeModel model, PredictionSlot slot) => [new PredictionCandidate("a", 1)];

        public IReadOnlySet<string> Actual(IReadOnlyList<int> samples, PredictionSlot slot) =>
            new HashSet<string>(StringComparer.Ordinal) { "a" };
    }

    private sealed record FakeStrategy(string Label) : IStrategy;

    private sealed record FakeModel(double AsOf);

    private sealed class LagDomain : IPredictionDomain<double, FakeStrategy, FakeModel> {
        public readonly List<double> TrainedAsOf = [];

        public string Name => "lag";

        public IReadOnlyList<FakeStrategy> Strategies => [new FakeStrategy("default")];

        public IReadOnlyList<PredictionSlot> Slots(IReadOnlyList<double> samples, double from, double to) =>
            [new PredictionSlot(from, "a"), new PredictionSlot(to, "a")];

        public FakeModel Train(IReadOnlyList<double> samples, double asOf, FakeStrategy strategy) {
            TrainedAsOf.Add(asOf);
            return new FakeModel(asOf);
        }

        public IReadOnlyList<PredictionCandidate> Odds(FakeModel model, PredictionSlot slot) =>
            [new PredictionCandidate("hit", 0.6), new PredictionCandidate("miss", 0.4)];

        public IReadOnlySet<string> Actual(IReadOnlyList<double> samples, PredictionSlot slot) =>
            new HashSet<string>(StringComparer.Ordinal) { "hit" };
    }

    private sealed class ArithmeticDomain : IPredictionDomain<int, FakeStrategy, FakeModel> {
        public string Name => "arithmetic";

        public IReadOnlyList<FakeStrategy> Strategies => [new FakeStrategy("x")];

        public IReadOnlyList<PredictionSlot> Slots(IReadOnlyList<int> samples, double from, double to) =>
            [new PredictionSlot(0, "k"), new PredictionSlot(50, "k")];

        public FakeModel Train(IReadOnlyList<int> samples, double asOf, FakeStrategy strategy) => new(asOf);

        public IReadOnlyList<PredictionCandidate> Odds(FakeModel model, PredictionSlot slot) =>
            [new PredictionCandidate("a", 0.5), new PredictionCandidate("b", 0.3), new PredictionCandidate("c", 0.2)];

        public IReadOnlySet<string> Actual(IReadOnlyList<int> samples, PredictionSlot slot) =>
            slot.Time == 0 ? Set("a") : Set("c");

        private static HashSet<string> Set(string key) => new([key], StringComparer.Ordinal);
    }

    [Fact]
    public void Run_EverySlot_TrainsOnlyAtOrBeforeTheLaggedSlotTime() {
        var domain = new LagDomain();
        double lag = 10;
        double from = 0;
        double to = 100;

        WalkForward.Run(domain, [], domain.Strategies[0], lag, from, to);

        var slots = domain.Slots([], from, to);
        Assert.NotEmpty(domain.TrainedAsOf);
        Assert.All(domain.TrainedAsOf, asOf => Assert.Contains(slots, s => asOf <= s.Time - lag));
    }

    [Fact]
    public void Run_TwoHandBuiltSlots_ComputesTopKAndLogLoss() {
        var domain = new ArithmeticDomain();

        var result = WalkForward.Run(domain, [], domain.Strategies[0], 0, 0, 50);

        Assert.Equal(2, result.Total.Slots);
        Assert.Equal(1, result.Total.Top1);
        Assert.Equal(2, result.Total.Top3);
        Assert.Equal(2, result.Total.Top5);
        Assert.Equal((-Math.Log(0.5) + -Math.Log(0.2)) / 2, result.Total.LogLoss, 6);
        Assert.Equal(0, result.Uncovered);
    }

    [Fact]
    public void Run_ManySlots_ReportsProgressAtStartAndEveryTenSlots() {
        var domain = new ManySlotsDomain();
        var reports = new List<TrainingProgress>();
        var progress = new DelegateProgress<TrainingProgress>(reports.Add);

        WalkForward.Run(domain, [], domain.Strategies[0], 0, 0, 24, progress);

        Assert.Equal(new TrainingProgress(0, 1, 0, 25), reports[0]);
        Assert.Contains(reports, p => p.SlotIndex == 10);
        Assert.Contains(reports, p => p.SlotIndex == 20);
    }

    [Fact]
    public void Run_CancelledToken_StopsEarly() {
        var domain = new ManySlotsDomain();
        using var cts = new CancellationTokenSource();
        var progress = new DelegateProgress<TrainingProgress>(p => {
            if (p.SlotIndex >= 10) cts.Cancel();
        });

        Assert.Throws<OperationCanceledException>(
            () => WalkForward.Run(domain, [], domain.Strategies[0], 0, 0, 24, progress, cts.Token));
    }
}
