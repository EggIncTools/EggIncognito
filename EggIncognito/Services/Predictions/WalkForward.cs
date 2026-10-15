using EggIncognito.Services.Predictions.Training;

namespace EggIncognito.Services.Predictions;

public static class WalkForward {
    public const double DefaultLagSeconds = 30 * 86400d;
    public const double DefaultRangeSeconds = 365 * 86400d;
    private const double LogLossFloor = 1e-3;
    private const int ProgressEverySlots = 10;

    public static WalkForwardResult Run<TSample, TStrategy, TModel>(
        IPredictionDomain<TSample, TStrategy, TModel> domain,
        IReadOnlyList<TSample> samples,
        TStrategy strategy,
        double lagSeconds,
        double from,
        double to,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken ct = default) where TStrategy : notnull, IStrategy {
        var slots = domain.Slots(samples, from, to).OrderBy(s => s.Time).ToList();
        progress?.Report(new TrainingProgress(0, 1, 0, slots.Count));
        var models = new Dictionary<double, TModel>();
        var byKind = new Dictionary<string, (int Slots, int Top1, int Top3, int Top5, double LogLossSum)>(StringComparer.Ordinal);
        int uncovered = 0;
        int totalSlots = 0, totalTop1 = 0, totalTop3 = 0, totalTop5 = 0;
        double totalLogLoss = 0;
        int processed = 0;

        foreach (var slot in slots) {
            ct.ThrowIfCancellationRequested();
            var actual = domain.Actual(samples, slot);
            if (actual.Count == 0) {
                uncovered++;
            } else {
                double trainAsOf = slot.Time - lagSeconds;
                if (!models.TryGetValue(trainAsOf, out var model)) models[trainAsOf] = model = domain.Train(samples, trainAsOf, strategy);

                var ranked = domain.Odds(model, slot).OrderByDescending(c => c.Probability).ToList();
                double probability = Math.Min(1, ranked.Where(c => actual.Contains(c.Key)).Sum(c => c.Probability));
                double logLoss = -Math.Log(Math.Max(probability, LogLossFloor));
                bool top1 = ranked.Count > 0 && actual.Contains(ranked[0].Key);
                bool top3 = ranked.Take(3).Any(c => actual.Contains(c.Key));
                bool top5 = ranked.Take(5).Any(c => actual.Contains(c.Key));

                var current = byKind.GetValueOrDefault(slot.Kind);
                byKind[slot.Kind] = (
                    current.Slots + 1,
                    current.Top1 + (top1 ? 1 : 0),
                    current.Top3 + (top3 ? 1 : 0),
                    current.Top5 + (top5 ? 1 : 0),
                    current.LogLossSum + logLoss);

                totalSlots++;
                totalTop1 += top1 ? 1 : 0;
                totalTop3 += top3 ? 1 : 0;
                totalTop5 += top5 ? 1 : 0;
                totalLogLoss += logLoss;
            }

            processed++;
            if (progress is not null && processed % ProgressEverySlots == 0)
                progress.Report(new TrainingProgress(0, 1, processed, slots.Count));
        }

        var kinds = byKind
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new WalkForwardKindResult(
                kv.Key, kv.Value.Slots, kv.Value.Top1, kv.Value.Top3, kv.Value.Top5,
                kv.Value.Slots > 0 ? kv.Value.LogLossSum / kv.Value.Slots : 0))
            .ToList();
        var total = new WalkForwardKindResult(
            "total", totalSlots, totalTop1, totalTop3, totalTop5, totalSlots > 0 ? totalLogLoss / totalSlots : 0);

        return new WalkForwardResult(from, to, lagSeconds, kinds, total, uncovered);
    }
}
