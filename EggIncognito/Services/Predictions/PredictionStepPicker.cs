namespace EggIncognito.Services.Predictions;

internal static class PredictionStepPicker {
    public static int Pick(IEnumerable<int> steps, int? requested) {
        var ordered = steps.OrderBy(s => s).ToList();
        if (ordered.Count == 0) throw new InvalidOperationException("no precomputed steps available");
        if (requested is not { } wanted) return ordered[^1];
        return ordered.LastOrDefault(s => s <= wanted, ordered[0]);
    }
}
