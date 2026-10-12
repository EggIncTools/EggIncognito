using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions.Contracts;

public static class QueueRanks {
    public const double WindowSeconds = 365 * 86400d;
    private const double Smoothing = 0.5;

    public static IReadOnlyDictionary<ContractSlotKind, IReadOnlyList<double>> Fit(
        IReadOnlyList<ContractReleaseSample> samples, double asOf) {
        var counts = ReleaseGrid.AllKinds.ToDictionary(k => k, _ => new List<int>());
        var releases = samples
            .Where(s => s.Start < asOf && s.Start >= asOf - WindowSeconds && s.ReleaseKind != ContractSlotKind.NewContract)
            .GroupBy(s => s.Start)
            .OrderBy(g => g.Key);
        foreach (var batch in releases) {
            var pools = OldestFirstPools.Fit([.. samples.Where(s => s.Start < batch.Key)]);
            foreach (var release in batch) {
                var queue = OldestFirstPools.Eligible(pools[release.ReleaseKind], release.Start);
                int rank = queue.FindIndex(c => c.ContractId == release.ContractId);
                if (rank >= 0) counts[release.ReleaseKind].Add(rank);
            }
        }

        return counts.ToDictionary(kv => kv.Key, kv => Distribution(kv.Value));
    }

    private static IReadOnlyList<double> Distribution(List<int> ranks) {
        if (ranks.Count == 0) return [1];
        int width = ranks.Max() + 2;
        var hist = new double[width];
        foreach (int r in ranks) hist[r]++;
        double total = ranks.Count + Smoothing * width;
        return [.. hist.Select(h => (h + Smoothing) / total)];
    }
}
