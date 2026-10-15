using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions.Contracts;

public sealed class ContractDomain : IPredictionDomain<ContractReleaseSample, ContractStrategy, ContractModel> {
    public const double SixHoursSeconds = 6 * 3600d;

    public static readonly ContractSlotKind[] PredictedKinds =
        [ContractSlotKind.Leggacy, ContractSlotKind.PeLeggacy, ContractSlotKind.PeLeggacyUltra];

    public string Name => "contracts";

    public IReadOnlyList<ContractStrategy> Strategies => ContractStrategy.Grid;

    public IReadOnlyList<PredictionSlot> Slots(IReadOnlyList<ContractReleaseSample> samples, double from, double to) {
        var slots = new List<PredictionSlot>();
        var start = NoonEastern.LocalDate(from);
        var end = NoonEastern.LocalDate(to);
        for (var day = start; day <= end; day = day.AddDays(1)) {
            foreach (var kind in PredictedKinds) {
                if (day.DayOfWeek != ReleaseGrid.WeekdayOf(kind)) continue;
                double time = NoonEastern.SlotTime(day);
                if (time < from || time > to) continue;
                if (samples.Any(s => s.ReleaseKind == kind && Math.Abs(s.Start - time) <= SixHoursSeconds))
                    slots.Add(new PredictionSlot(time, kind.ToString()));
            }
        }

        return [.. slots.OrderBy(s => s.Time)];
    }

    public ContractModel Train(IReadOnlyList<ContractReleaseSample> samples, double asOf, ContractStrategy strategy) =>
        ContractModel.Train(samples, asOf, strategy);

    public IReadOnlyList<PredictionCandidate> Odds(ContractModel model, PredictionSlot slot) {
        var kind = Enum.Parse<ContractSlotKind>(slot.Kind);
        var pool = model.Pools[kind];
        var eligible = OldestFirstPools.Eligible(pool, slot.Time, model.Strategy, model.RankFit.NeverRerun);
        var ranks = model.RankFit.Ranks[kind];
        var result = new List<PredictionCandidate>(Math.Min(eligible.Count, ranks.Count));
        for (int r = 0; r < eligible.Count && r < ranks.Count; r++) result.Add(new PredictionCandidate(eligible[r].ContractId, ranks[r]));
        return result;
    }

    public IReadOnlySet<string> Actual(IReadOnlyList<ContractReleaseSample> samples, PredictionSlot slot) {
        var kind = Enum.Parse<ContractSlotKind>(slot.Kind);
        return samples
            .Where(s => s.ReleaseKind == kind && Math.Abs(s.Start - slot.Time) <= SixHoursSeconds)
            .Select(s => s.ContractId)
            .ToHashSet(StringComparer.Ordinal);
    }
}
