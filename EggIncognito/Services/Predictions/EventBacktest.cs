using EggIncognito.Models.Events;
using EggIncognito.Services.Predictions.Events;

namespace EggIncognito.Services.Predictions;

public static class EventBacktest {
    public const int SweepWindows = 6;
    public const int SweepHorizonDays = 28;

    public static EventBacktestResult Run(IReadOnlyList<EventRow> rows, double asOf, int horizonDays) {
        int horizon = Math.Clamp(horizonDays, EventProjector.MinHorizonDays, EventProjector.MaxHorizonDays);
        double end = asOf + horizon * EventHistory.Day;
        var predictions = EventPredictor.Predict(rows, asOf, horizon);
        var actual = rows.Where(r => r.Start >= asOf && r.Start < end).ToList();
        var byDate = actual
            .GroupBy(r => (r.Ultra, Date: NoonEastern.LocalDate(r.Start)))
            .ToDictionary(g => g.Key, g => g.Select(r => r.Type).ToHashSet(StringComparer.Ordinal));

        var kinds = new List<EventBacktestKindResult>();
        foreach (var kind in Enum.GetValues<EventRuleKind>()) {
            int predicted = 0, slotHit = 0, typeHit = 0, top3Hit = 0;
            foreach (var p in predictions.Where(p => p.Kind == kind)) {
                predicted++;
                if (!byDate.TryGetValue((p.Ultra, NoonEastern.LocalDate(p.PredictedStart)), out var types)) continue;
                slotHit++;
                if (p.Type is { } top && types.Contains(top)) typeHit++;
                if (p.Candidates.Take(3).Any(c => types.Contains(c.Type))) top3Hit++;
            }

            kinds.Add(new EventBacktestKindResult(kind, predicted, slotHit, typeHit, top3Hit));
        }

        var covered = predictions
            .Select(p => (p.Ultra, Date: NoonEastern.LocalDate(p.PredictedStart)))
            .ToHashSet();
        int uncovered = actual.Count(r => !covered.Contains((r.Ultra, NoonEastern.LocalDate(r.Start))));
        return new EventBacktestResult(asOf, horizon, kinds, uncovered);
    }

    public static EventBacktestSweep Sweep(IReadOnlyList<EventRow> rows, double asOf, int windows = SweepWindows) {
        var results = new List<EventBacktestResult>(windows);
        for (int i = 1; i <= windows; i++) {
            results.Add(Run(rows, asOf - i * SweepHorizonDays * EventHistory.Day, SweepHorizonDays));
        }

        var totals = Enum.GetValues<EventRuleKind>()
            .Select(kind => {
                var rows2 = results.SelectMany(r => r.Kinds).Where(k => k.Kind == kind).ToList();
                return new EventBacktestKindResult(
                    kind, rows2.Sum(k => k.Predicted), rows2.Sum(k => k.SlotHit),
                    rows2.Sum(k => k.TypeHit), rows2.Sum(k => k.Top3Hit));
            })
            .ToList();
        return new EventBacktestSweep(results, totals);
    }
}
