namespace EggIncognito.Services.Predictions.Events;

public static class PoolHazard {
    public const double NearDays = 1;

    public static IReadOnlyDictionary<string, IReadOnlyList<double>> GapsByType(IEnumerable<EventOccurrence> occurrences) {
        var result = new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal);
        foreach (var group in occurrences.GroupBy(o => o.Type, StringComparer.Ordinal)) {
            var dates = group.Select(o => o.Date).Distinct().Order().ToList();
            var gaps = new List<double>(dates.Count);
            for (int i = 1; i < dates.Count; i++) gaps.Add(dates[i].DayNumber - dates[i - 1].DayNumber);
            result[group.Key] = gaps;
        }

        return result;
    }

    public static IReadOnlyDictionary<string, int> CountsByType(IEnumerable<EventOccurrence> occurrences) =>
        occurrences.GroupBy(o => o.Type, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    public static double Hazard(IReadOnlyList<double> gaps, double daysSince, double smoothing) {
        if (gaps.Count == 0) return 0;
        if (daysSince > gaps.Max() + NearDays) return smoothing / (gaps.Count + 1);
        int atOrAfter = gaps.Count(g => g >= daysSince - NearDays);
        int near = gaps.Count(g => Math.Abs(g - daysSince) <= NearDays);
        return (near + smoothing) / (atOrAfter + 1);
    }

    public static double WeekdayPrior(IReadOnlyDictionary<string, int> counts, string type, double smoothing) {
        double total = counts.Values.Sum();
        return (counts.GetValueOrDefault(type) + smoothing) / (total + smoothing * Math.Max(counts.Count, 1));
    }

    public static double DistanceHistogram(IReadOnlyList<double> gaps, double daysSince, double smoothing) {
        if (gaps.Count == 0) return 0;
        double near = gaps.Count(g => Math.Abs(g - daysSince) <= NearDays);
        return (near + smoothing) / (gaps.Count + smoothing);
    }
}
