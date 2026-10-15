using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions.Contracts;

public sealed record EggQueueEntry(string EggId, double LastReleased);

public sealed record ColleggtibleModel(
    double TrainedAt,
    ColleggtibleStrategy Strategy,
    ContractModel? Contracts,
    IReadOnlyDictionary<string, string> EggByContract,
    IReadOnlyList<EggQueueEntry> EggQueue,
    IReadOnlyList<double> EggRanks) {
    private const double WindowSeconds = 365 * 86400d;
    private const double Smoothing = 0.5;

    public static ColleggtibleModel Train(
        IReadOnlyList<ContractReleaseSample> samples, double asOf, ColleggtibleStrategy strategy, ContractStrategy contractStrategy) =>
        Train(samples, asOf, strategy, QueueRanks.Trace(samples, contractStrategy));

    public static ColleggtibleModel Train(
        IReadOnlyList<ContractReleaseSample> samples, double asOf, ColleggtibleStrategy strategy, QueueRankTrace contractTrace) {
        var eggByContract = samples
            .Where(s => s.CustomEggId is not null)
            .GroupBy(s => s.ContractId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().CustomEggId ?? "", StringComparer.Ordinal);

        if (strategy.Grouping == ColleggtibleGrouping.PerContract) {
            return new ColleggtibleModel(
                asOf, strategy, ContractModel.Train(samples, asOf, contractTrace), eggByContract, [], []);
        }

        var past = samples.Where(s => s.Start < asOf && s.CustomEggId is not null).ToList();
        var collapsed = Collapse(past, strategy.MergePeAndStandard);
        var queue = BuildEggQueue(collapsed);
        var ranks = FitEggRanks(collapsed, asOf);
        return new ColleggtibleModel(asOf, strategy, null, eggByContract, queue, ranks);
    }

    public IReadOnlyList<PredictionCandidate> Odds(PredictionSlot slot) {
        if (Strategy.Grouping == ColleggtibleGrouping.PerContract) return PerContractOdds(slot);

        var result = new List<PredictionCandidate>(Math.Min(EggQueue.Count, EggRanks.Count));
        for (int r = 0; r < EggQueue.Count && r < EggRanks.Count; r++) result.Add(new PredictionCandidate(EggQueue[r].EggId, EggRanks[r]));
        return result;
    }

    private IReadOnlyList<PredictionCandidate> PerContractOdds(PredictionSlot slot) {
        if (Contracts is null) return [];
        var kind = Enum.Parse<ContractSlotKind>(slot.Kind);
        var pool = Contracts.Pools[kind];
        var eligible = OldestFirstPools.Eligible(pool, slot.Time, Contracts.Strategy, Contracts.RankFit.NeverRerun);
        var ranks = Contracts.RankFit.Ranks[kind];
        var byEgg = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int r = 0; r < eligible.Count && r < ranks.Count; r++) {
            if (!EggByContract.TryGetValue(eligible[r].ContractId, out var egg)) continue;
            byEgg[egg] = byEgg.GetValueOrDefault(egg) + ranks[r];
        }

        return [.. byEgg.Select(kv => new PredictionCandidate(kv.Key, Math.Min(1, kv.Value)))];
    }

    private static IReadOnlyList<ContractReleaseSample> Collapse(IReadOnlyList<ContractReleaseSample> eggs, bool merge) {
        if (!merge) return eggs;
        return [
            .. eggs.GroupBy(s => (s.CustomEggId, Day: Math.Floor(s.Start / 86400d)))
                .Select(g => g.OrderBy(s => s.Start).First())
        ];
    }

    private static IReadOnlyList<EggQueueEntry> BuildEggQueue(IReadOnlyList<ContractReleaseSample> eggs) {
        return [
            .. eggs.GroupBy(s => s.CustomEggId ?? "", StringComparer.Ordinal)
                .Select(g => new EggQueueEntry(g.Key, g.Max(s => s.Start)))
                .OrderBy(e => e.LastReleased)
        ];
    }

    private static IReadOnlyList<double> FitEggRanks(IReadOnlyList<ContractReleaseSample> eggs, double asOf) {
        var counts = new List<int>();
        var last = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var batch in eggs.OrderBy(s => s.Start).GroupBy(s => s.Start)) {
            if (batch.Key >= asOf - WindowSeconds) {
                var queue = last.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key).ToList();
                var taken = new HashSet<string>(StringComparer.Ordinal);
                foreach (var release in batch) {
                    string egg = release.CustomEggId ?? "";
                    int rank = queue.Where(e => !taken.Contains(e)).ToList().IndexOf(egg);
                    if (rank >= 0) counts.Add(rank);
                    taken.Add(egg);
                }
            }

            foreach (var release in batch) last[release.CustomEggId ?? ""] = release.Start;
        }

        if (counts.Count == 0) return [1];
        int width = counts.Max() + 2;
        var hist = new double[width];
        foreach (int r in counts) hist[r]++;
        double total = counts.Count + Smoothing * width;
        return [.. hist.Select(h => (h + Smoothing) / total)];
    }
}
