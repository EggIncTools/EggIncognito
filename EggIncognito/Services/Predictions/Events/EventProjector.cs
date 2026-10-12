using EggIncognito.Models.Events;

namespace EggIncognito.Services.Predictions.Events;

public static class EventProjector {
    public const int MinHorizonDays = 1;
    public const int MaxHorizonDays = 90;
    public const int Paths = 1000;
    private const int Seed = 20261011;

    public static IReadOnlyList<EventPrediction> Project(
        EventModel model, EventHistory history, double asOf, int horizonDays) {
        int horizon = Math.Clamp(horizonDays, MinHorizonDays, MaxHorizonDays);
        double horizonEnd = asOf + horizon * EventHistory.Day;
        var root = EventLedger.From(history);
        var plans = model.Rules
            .SelectMany(rule => rule.Dates(asOf, horizonEnd).Select(date => (Rule: rule, Date: date)))
            .DistinctBy(p => (p.Rule.Key, p.Date))
            .OrderBy(p => NoonEastern.SlotTime(p.Date))
            .ThenBy(p => p.Rule.Rank)
            .ThenBy(p => p.Rule.Key, StringComparer.Ordinal)
            .ToList();
        var picks = new string?[Paths, plans.Count];
        var random = new Random(Seed);
        for (int path = 0; path < Paths; path++) {
            var ledger = root.Clone();
            for (int i = 0; i < plans.Count; i++) {
                var (rule, date) = plans[i];
                if (Sample(rule.Odds(date, ledger), random.NextDouble()) is not { } type) continue;
                ledger.Touch(type, rule.Ultra, NoonEastern.SlotTime(date), date);
                picks[path, i] = type;
            }
        }

        var consistent = Enumerable.Range(0, Paths).ToList();
        var predictions = new List<EventPrediction>(plans.Count);
        for (int i = 0; i < plans.Count; i++) {
            var (rule, date) = plans[i];
            var candidates = Enumerable.Range(0, Paths)
                .Select(path => picks[path, i])
                .OfType<string>()
                .GroupBy(t => t, StringComparer.Ordinal)
                .Select(g => new EventCandidate(g.Key, g.Count() / (double)Paths))
                .OrderByDescending(c => c.Probability)
                .ThenBy(c => c.Type, StringComparer.Ordinal)
                .ToList();
            if (candidates.Count == 0) continue;
            bool fixedType = rule.Type is not null || rule.Kind == EventRuleKind.Alternating;
            var top = candidates[0];
            if (!fixedType && CoherentPick(picks, i, consistent) is { } pick) {
                top = candidates.First(c => c.Type == pick);
                candidates = [top, .. candidates.Where(c => c != top)];
                int col = i;
                var kept = consistent.Where(path => picks[path, col] == pick).ToList();
                if (kept.Count > 0) consistent = kept;
            }

            double occurs = candidates.Sum(c => c.Probability);
            double start = NoonEastern.SlotTime(date);
            predictions.Add(new EventPrediction(
                top.Type, rule.Ultra, rule.Kind, rule.Key, rule.Evidence.Summary, start, start + rule.Duration(top.Type),
                fixedType ? occurs : top.Probability,
                fixedType ? [top with { Probability = 1 }] : candidates,
                rule.Evidence.Observed, rule.Evidence.Expected, rule.PeriodDays, rule.Evidence.LastStart));
        }

        return predictions;
    }

    private static string? CoherentPick(string?[,] picks, int column, List<int> paths) =>
        paths.Select(path => picks[path, column])
            .OfType<string>()
            .GroupBy(t => t, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .FirstOrDefault()?.Key;

    private static string? Sample(IReadOnlyList<EventCandidate> odds, double u) {
        foreach (var c in odds) {
            if (u < c.Probability) return c.Type;
            u -= c.Probability;
        }

        return null;
    }
}
