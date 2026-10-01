using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions.Contracts;

public static class OldestFirstPools {
    public const int TopCandidates = 5;
    public const int MinGapSamples = 4;
    public const double RecencyGapMultiplier = 2d;
    public const double FallbackCutoffSeconds = 3 * 365.25d * 86400d;
    private const double DefaultLengthSeconds = 7 * 86400d;

    public static IReadOnlyDictionary<ContractSlotKind, ContractPool> Fit(IReadOnlyList<ContractReleaseSample> samples) {
        var members = ReleaseGrid.AllKinds.ToDictionary(k => k, _ => new List<ContractCandidate>());
        var gaps = ReleaseGrid.AllKinds.ToDictionary(k => k, _ => new List<double>());
        var lengths = ReleaseGrid.AllKinds.ToDictionary(k => k, _ => new List<double>());

        foreach (var sample in samples) lengths[sample.ReleaseKind].Add(sample.LengthSeconds);
        foreach (var group in samples.GroupBy(s => s.ContractId, StringComparer.Ordinal)) {
            var ordered = group.OrderBy(s => s.Start).ToList();
            var newest = ordered[^1];
            var pool = NextKind(ordered);
            members[pool].Add(new ContractCandidate(group.Key, newest.Name, newest.Start, ordered.Count));
            for (int i = 1; i < ordered.Count; i++) gaps[pool].Add(ordered[i].Start - ordered[i - 1].Start);
        }

        var pools = new Dictionary<ContractSlotKind, ContractPool>();
        foreach (var kind in ReleaseGrid.AllKinds) {
            var candidates = members[kind]
                .OrderBy(c => c.LastReleased)
                .ThenBy(c => c.Releases)
                .ThenBy(c => c.ContractId, StringComparer.Ordinal)
                .ToList();
            double? gap = kind == ContractSlotKind.NewContract || gaps[kind].Count < MinGapSamples
                ? null
                : RobustStats.Median(gaps[kind]);
            double length = lengths[kind].Count > 0
                ? RobustStats.Median(lengths[kind].Where(l => l > 0).ToList())
                : DefaultLengthSeconds;
            if (length <= 0) length = DefaultLengthSeconds;
            pools[kind] = new ContractPool(
                kind, candidates, gap, gaps[kind].Count, length,
                new RuleEvidence(candidates.Count, candidates.Count, candidates.Count > 0 ? candidates[^1].LastReleased : 0,
                    Summary(kind, candidates.Count, gap, gaps[kind].Count)));
        }

        return pools;
    }

    public static ContractSlotKind NextKind(IReadOnlyList<ContractReleaseSample> ordered) {
        if (ordered.Max(s => s.ProphecyEggs) <= 0) return ContractSlotKind.Leggacy;
        return ordered[^1].UltraOnly ? ContractSlotKind.PeLeggacy : ContractSlotKind.PeLeggacyUltra;
    }

    public static IReadOnlyList<ContractCandidate> Top(ContractPool pool, double now, int skip = 0) {
        double cutoff = pool.GapSeconds is { } gap ? RecencyGapMultiplier * gap : FallbackCutoffSeconds;
        return pool.Candidates.Where(c => now - c.LastReleased <= cutoff).Skip(skip).Take(TopCandidates).ToList();
    }

    private static string Summary(ContractSlotKind kind, int count, double? gap, int gapSamples) {
        string cadence = gap is { } g
            ? $", re-released every ~{Math.Round(g / (7 * 86400d))} weeks ({gapSamples} gaps)"
            : "";
        return kind switch {
            ContractSlotKind.NewContract => "New contract: never seen before, no candidates",
            ContractSlotKind.Leggacy => $"Leggacy without PE: oldest last release first, {count} contracts{cadence}",
            ContractSlotKind.PeLeggacy =>
                $"PE leggacy, standard: contracts whose last run was ultra-only, oldest first, {count} contracts{cadence}",
            _ => $"PE leggacy, ultra-only: contracts whose last run was standard, oldest first, {count} contracts{cadence}"
        };
    }
}
