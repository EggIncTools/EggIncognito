using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions.Contracts;

public sealed record QueueRankFit(
    IReadOnlyDictionary<ContractSlotKind, IReadOnlyList<double>> Ranks,
    IReadOnlySet<string> NeverRerun);

public static class QueueRanks {
    public const int NeverRerunThreshold = 6;

    public static QueueRankFit Fit(IReadOnlyList<ContractReleaseSample> samples, double asOf, ContractStrategy strategy) {
        double window = strategy.RankWindowDays * 86400d;
        var counts = ReleaseGrid.AllKinds.ToDictionary(k => k, _ => new List<int>());
        var misses = new Dictionary<string, int>(StringComparer.Ordinal);
        var releases = samples
            .Where(s => s.Start < asOf && s.Start >= asOf - window && s.ReleaseKind != ContractSlotKind.NewContract)
            .GroupBy(s => s.Start)
            .OrderBy(g => g.Key);
        foreach (var batch in releases) {
            var pools = OldestFirstPools.Fit([.. samples.Where(s => s.Start < batch.Key)], strategy);
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var release in batch.OrderBy(r => Array.IndexOf(ReleaseGrid.AllKinds, r.ReleaseKind))) {
                var queue = OldestFirstPools.Eligible(pools[release.ReleaseKind], release.Start, strategy)
                    .Where(c => !taken.Contains(c.ContractId)).ToList();
                int rank = queue.FindIndex(c => c.ContractId == release.ContractId);
                if (rank >= 0) counts[release.ReleaseKind].Add(rank);
                for (int i = 0; i < Math.Min(2, queue.Count); i++) {
                    if (queue[i].ContractId == release.ContractId) continue;
                    misses[queue[i].ContractId] = misses.GetValueOrDefault(queue[i].ContractId) + 1;
                }
                misses[release.ContractId] = 0;
                taken.Add(release.ContractId);
            }
        }

        var neverRerun = misses.Where(kv => kv.Value >= NeverRerunThreshold).Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);
        var ranks = counts.ToDictionary(kv => kv.Key, kv => Distribution(kv.Value, strategy.RankSmoothing));
        return new QueueRankFit(ranks, neverRerun);
    }

    private static IReadOnlyList<double> Distribution(List<int> ranks, double smoothing) {
        if (ranks.Count == 0) return [1];
        int width = ranks.Max() + 2;
        var hist = new double[width];
        foreach (int r in ranks) hist[r]++;
        double total = ranks.Count + smoothing * width;
        return [.. hist.Select(h => (h + smoothing) / total)];
    }
}
