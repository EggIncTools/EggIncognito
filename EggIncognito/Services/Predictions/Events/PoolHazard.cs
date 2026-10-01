namespace EggIncognito.Services.Predictions.Events;

public static class PoolHazard {
    public const double NearDays = 1;
    private const double Smoothing = 0.5;

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

    public static double Hazard(IReadOnlyList<double> gaps, double daysSince) {
        if (gaps.Count == 0) return 0;
        if (daysSince > gaps.Max() + NearDays) return Smoothing / (gaps.Count + 1);
        int atOrAfter = gaps.Count(g => g >= daysSince - NearDays);
        int near = gaps.Count(g => Math.Abs(g - daysSince) <= NearDays);
        return (near + Smoothing) / (atOrAfter + 1);
    }
}
