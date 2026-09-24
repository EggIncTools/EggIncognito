namespace EggIncognito.Services.Predictions;

public static class RobustStats {
    public static double Median(IReadOnlyList<double> xs) {
        if (xs.Count == 0) return 0;
        var sorted = xs.Order().ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    public static double Mad(IReadOnlyList<double> xs, double median) =>
        Median(xs.Select(x => Math.Abs(x - median)).ToList());
}
