using EggIncognito.Models.Contracts;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions.Contracts;

namespace EggIncognito.Services.Predictions;

public static class ContractBacktest {
    public const int MinHorizonSlots = 1;
    public const int MaxHorizonSlots = 30;
    public const int SweepWindows = 6;
    public const int SweepHorizonSlots = 9;
    public const double SweepStepSeconds = 21 * 86400d;
    private const double SlotToleranceSeconds = 6 * 3600d;

    public static ContractBacktestResult Run(IReadOnlyList<ContractReleaseSample> samples, double asOf, int horizonSlots) {
        int horizon = Math.Clamp(horizonSlots, MinHorizonSlots, MaxHorizonSlots);
        var training = samples.Where(s => s.Start < asOf).ToList();
        var model = ContractModel.Train(training, asOf);
        var slots = model.Slots(UnixSeconds.ToTime(asOf), horizon, asOf);
        double end = slots.Count > 0 ? slots.Max(s => s.SlotTime) + SlotToleranceSeconds : asOf;
        var actual = samples.Where(s => s.Start >= asOf && s.Start <= end).ToList();

        var kinds = new List<ContractBacktestKindResult>();
        var covered = new HashSet<ContractReleaseSample>();
        foreach (var kind in ReleaseGrid.AllKinds) {
            int predicted = 0, slotHit = 0, top1 = 0, top5 = 0;
            foreach (var slot in slots.Where(s => s.Kind == kind)) {
                predicted++;
                var hits = actual
                    .Where(a => a.ReleaseKind == kind && Math.Abs(a.Start - slot.SlotTime) <= SlotToleranceSeconds)
                    .ToList();
                if (hits.Count == 0) continue;
                slotHit++;
                foreach (var hit in hits) covered.Add(hit);
                var ids = hits.Select(h => h.ContractId).ToHashSet(StringComparer.Ordinal);
                if (slot.Candidates.Count > 0 && ids.Contains(slot.Candidates[0].ContractId)) top1++;
                if (slot.Candidates.Any(c => ids.Contains(c.ContractId))) top5++;
            }

            kinds.Add(new ContractBacktestKindResult(kind, predicted, slotHit, top1, top5));
        }

        return new ContractBacktestResult(asOf, horizon, kinds, actual.Count(a => !covered.Contains(a)));
    }

    public static ContractBacktestSweep Sweep(
        IReadOnlyList<ContractReleaseSample> samples, double asOf, int windows = SweepWindows) {
        var results = new List<ContractBacktestResult>(windows);
        for (int i = 1; i <= windows; i++) results.Add(Run(samples, asOf - i * SweepStepSeconds, SweepHorizonSlots));
        var totals = ReleaseGrid.AllKinds
            .Select(kind => {
                var rows = results.SelectMany(r => r.Kinds).Where(k => k.Kind == kind).ToList();
                return new ContractBacktestKindResult(
                    kind, rows.Sum(k => k.Predicted), rows.Sum(k => k.SlotHit), rows.Sum(k => k.Top1Hit),
                    rows.Sum(k => k.Top5Hit));
            })
            .ToList();
        return new ContractBacktestSweep(results, totals);
    }
}
