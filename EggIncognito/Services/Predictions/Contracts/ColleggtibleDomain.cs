using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions.Contracts;

public sealed class ColleggtibleDomain(ContractStrategy contractStrategy)
    : IPredictionDomain<ContractReleaseSample, ColleggtibleStrategy, ColleggtibleModel> {
    public string Name => "colleggtibles";

    public IReadOnlyList<ColleggtibleStrategy> Strategies => ColleggtibleStrategy.Grid;

    public IReadOnlyList<PredictionSlot> Slots(IReadOnlyList<ContractReleaseSample> samples, double from, double to) {
        var slots = new List<PredictionSlot>();
        foreach (var kind in ContractDomain.PredictedKinds) {
            var times = samples
                .Where(s => s.CustomEggId is not null && s.ReleaseKind == kind && s.Start >= from && s.Start <= to)
                .Select(s => s.Start)
                .Distinct();
            foreach (var time in times) slots.Add(new PredictionSlot(time, kind.ToString()));
        }

        return [.. slots.OrderBy(s => s.Time)];
    }

    private QueueRankTrace? _trace;
    private IReadOnlyList<ContractReleaseSample>? _traced;

    private readonly Lock _gate = new();

    public ColleggtibleModel Train(IReadOnlyList<ContractReleaseSample> samples, double asOf, ColleggtibleStrategy strategy) {
        QueueRankTrace trace;
        lock (_gate) {
            if (_trace is null || !ReferenceEquals(samples, _traced)) {
                _trace = QueueRanks.Trace(samples, contractStrategy);
                _traced = samples;
            }

            trace = _trace;
        }

        return ColleggtibleModel.Train(samples, asOf, strategy, trace);
    }

    public IReadOnlyList<PredictionCandidate> Odds(ColleggtibleModel model, PredictionSlot slot) => model.Odds(slot);

    public IReadOnlySet<string> Actual(IReadOnlyList<ContractReleaseSample> samples, PredictionSlot slot) {
        var kind = Enum.Parse<ContractSlotKind>(slot.Kind);
        return samples
            .Where(s => s.CustomEggId is not null && s.ReleaseKind == kind && Math.Abs(s.Start - slot.Time) <= ContractDomain.SixHoursSeconds)
            .Select(s => s.CustomEggId!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
