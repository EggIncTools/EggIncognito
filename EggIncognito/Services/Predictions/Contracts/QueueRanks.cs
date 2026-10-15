using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions.Contracts;

public sealed record QueueRankFit(
    IReadOnlyDictionary<ContractSlotKind, IReadOnlyList<double>> Ranks,
    IReadOnlySet<string> NeverRerun);

public sealed record QueueRankEntry(double Time, ContractSlotKind Kind, string ContractId, int Rank, IReadOnlyList<string> Missed);

public sealed record QueueRankTrace(ContractStrategy Strategy, IReadOnlyList<QueueRankEntry> Entries);

public static class QueueRanks {
    public const int NeverRerunThreshold = 6;

    public static QueueRankTrace Trace(IReadOnlyList<ContractReleaseSample> samples, ContractStrategy strategy) {
        var entries = new List<QueueRankEntry>();
        var history = new Dictionary<string, List<ContractReleaseSample>>(StringComparer.Ordinal);
        var gaps = ReleaseGrid.AllKinds.ToDictionary(k => k, _ => new List<double>());
        foreach (var batch in samples.OrderBy(s => s.Start).GroupBy(s => s.Start)) {
            var taken = new HashSet<string>(StringComparer.Ordinal);
            var pools = SnapshotPools(history, gaps, strategy);
            foreach (var release in batch.OrderBy(r => Array.IndexOf(ReleaseGrid.AllKinds, r.ReleaseKind))) {
                if (release.ReleaseKind != ContractSlotKind.NewContract) {
                    var queue = OldestFirstPools.Eligible(pools[release.ReleaseKind], release.Start, strategy)
                        .Where(c => !taken.Contains(c.ContractId)).ToList();
                    int rank = queue.FindIndex(c => c.ContractId == release.ContractId);
                    var missed = queue.Take(2).Select(c => c.ContractId).Where(id => id != release.ContractId).ToList();
                    entries.Add(new QueueRankEntry(release.Start, release.ReleaseKind, release.ContractId, rank, missed));
                }

                taken.Add(release.ContractId);
            }

            foreach (var release in batch) Record(history, gaps, release);
        }

        return new QueueRankTrace(strategy, entries);
    }

    public static QueueRankFit Fit(IReadOnlyList<ContractReleaseSample> samples, double asOf, ContractStrategy strategy) =>
        Fit(Trace(samples, strategy), asOf);

    public static QueueRankFit Fit(QueueRankTrace trace, double asOf) {
        double window = trace.Strategy.RankWindowDays * 86400d;
        var counts = ReleaseGrid.AllKinds.ToDictionary(k => k, _ => new List<int>());
        var misses = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in trace.Entries) {
            if (entry.Time >= asOf) break;
            if (entry.Time >= asOf - window && entry.Rank >= 0) counts[entry.Kind].Add(entry.Rank);
            foreach (string id in entry.Missed) misses[id] = misses.GetValueOrDefault(id) + 1;
            misses[entry.ContractId] = 0;
        }

        var neverRerun = misses.Where(kv => kv.Value >= NeverRerunThreshold).Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);
        var ranks = counts.ToDictionary(kv => kv.Key, kv => Distribution(kv.Value, trace.Strategy.RankSmoothing));
        return new QueueRankFit(ranks, neverRerun);
    }

    private static void Record(
        Dictionary<string, List<ContractReleaseSample>> history, Dictionary<ContractSlotKind, List<double>> gaps,
        ContractReleaseSample release) {
        if (!history.TryGetValue(release.ContractId, out var runs)) history[release.ContractId] = runs = [];
        AddGap(gaps, runs, release);
        runs.Add(release);
    }

    private static void AddGap(
        Dictionary<ContractSlotKind, List<double>> gaps, List<ContractReleaseSample> runs, ContractReleaseSample release) {
        if (runs.Count > 0) {
            double gap = release.Start - runs[^1].Start;
            bool pe = release.ProphecyEggs > 0 || runs.Any(s => s.ProphecyEggs > 0);
            foreach (var kind in pe ? OldestFirstPools.PeKinds : [ContractSlotKind.Leggacy]) gaps[kind].Add(gap);
        }
    }

    private static Dictionary<ContractSlotKind, ContractPool> SnapshotPools(
        Dictionary<string, List<ContractReleaseSample>> history, Dictionary<ContractSlotKind, List<double>> gaps,
        ContractStrategy strategy) {
        var members = ReleaseGrid.AllKinds.ToDictionary(k => k, _ => new List<ContractCandidate>());
        foreach (var (id, runs) in history) {
            var newest = runs[^1];
            var candidate = new ContractCandidate(id, newest.Name, newest.Start, runs.Count);
            foreach (var pool in OldestFirstPools.PoolsOf(runs, strategy)) members[pool].Add(candidate);
        }

        var pools = new Dictionary<ContractSlotKind, ContractPool>();
        foreach (var kind in ReleaseGrid.AllKinds) {
            var candidates = members[kind]
                .OrderBy(c => c.LastReleased)
                .ThenBy(c => c.Releases)
                .ThenBy(c => c.ContractId, StringComparer.Ordinal)
                .ToList();
            double? gap = kind == ContractSlotKind.NewContract || gaps[kind].Count < OldestFirstPools.MinGapSamples
                ? null
                : RobustStats.Median(gaps[kind]);
            pools[kind] = new ContractPool(kind, candidates, gap, gaps[kind].Count, 0, new RuleEvidence(0, 0, 0, ""));
        }

        return pools;
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
