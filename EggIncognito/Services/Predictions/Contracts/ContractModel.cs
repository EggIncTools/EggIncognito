using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions.Contracts;

public sealed record ContractModel(
    double TrainedAt,
    ContractStrategy Strategy,
    ReleaseGrid Grid,
    IReadOnlyDictionary<ContractSlotKind, ContractPool> Pools,
    QueueRankFit RankFit) {
    public const int Paths = 1000;
    private const int Seed = 20261011;

    public static ContractModel Train(IReadOnlyList<ContractReleaseSample> samples, double asOf, ContractStrategy strategy) {
        var past = samples.Where(s => s.Start < asOf).ToList();
        return new ContractModel(
            asOf, strategy, ReleaseGrid.Fit(samples, asOf), OldestFirstPools.Fit(past, strategy),
            QueueRanks.Fit(samples, asOf, strategy));
    }

    public IReadOnlyList<ContractSlotPrediction> Slots(DateTimeOffset from, int horizonSlots) {
        var grid = Grid.Next(from, horizonSlots).ToList();
        var tallies = grid.Select(_ => new Dictionary<string, int>(StringComparer.Ordinal)).ToList();
        var byId = Pools.Values.SelectMany(p => p.Candidates).DistinctBy(c => c.ContractId)
            .ToDictionary(c => c.ContractId, StringComparer.Ordinal);
        var random = new Random(Seed);
        for (int path = 0; path < Paths; path++) {
            var queues = Pools.ToDictionary(kv => kv.Key, kv => kv.Value.Candidates.ToList());
            for (int i = 0; i < grid.Count; i++) {
                var (time, slot) = grid[i];
                if (slot.Kind == ContractSlotKind.NewContract) continue;
                var eligible = OldestFirstPools.Eligible(
                    Pools[slot.Kind] with { Candidates = queues[slot.Kind] }, time, Strategy, RankFit.NeverRerun);
                if (eligible.Count == 0) continue;
                var pick = eligible[SampleRank(RankFit.Ranks[slot.Kind], eligible.Count, random.NextDouble())];
                tallies[i][pick.ContractId] = tallies[i].GetValueOrDefault(pick.ContractId) + 1;
                var released = pick with { LastReleased = time, Releases = pick.Releases + 1 };
                if (Strategy.PeMembership == PeMembership.Alternating && IsPe(slot.Kind)) {
                    queues[slot.Kind].RemoveAll(c => c.ContractId == pick.ContractId);
                    var next = slot.Kind == ContractSlotKind.PeLeggacy ? ContractSlotKind.PeLeggacyUltra : ContractSlotKind.PeLeggacy;
                    queues[next].RemoveAll(c => c.ContractId == pick.ContractId);
                    queues[next].Add(released);
                } else {
                    foreach (var kind in SharedPools(slot.Kind)) {
                        queues[kind].RemoveAll(c => c.ContractId == pick.ContractId);
                        queues[kind].Add(released);
                    }
                }
            }
        }

        var slots = new List<ContractSlotPrediction>(grid.Count);
        for (int i = 0; i < grid.Count; i++) {
            var (time, slot) = grid[i];
            var pool = Pools[slot.Kind];
            slots.Add(new ContractSlotPrediction(
                time, slot.Kind, pool.LengthSeconds,
                slot.Evidence.Summary + ". " + pool.Evidence.Summary,
                [
                    .. tallies[i]
                        .OrderByDescending(t => t.Value)
                        .ThenBy(t => t.Key, StringComparer.Ordinal)
                        .Take(OldestFirstPools.TopCandidates)
                        .Select(t => byId[t.Key] with { Probability = t.Value / (double)Paths })
                ]));
        }

        return slots;
    }

    private static bool IsPe(ContractSlotKind kind) => kind is ContractSlotKind.PeLeggacy or ContractSlotKind.PeLeggacyUltra;

    private static ContractSlotKind[] SharedPools(ContractSlotKind kind) =>
        IsPe(kind) ? OldestFirstPools.PeKinds : [kind];

    private static int SampleRank(IReadOnlyList<double> ranks, int count, double u) {
        int width = Math.Min(ranks.Count, count);
        double total = 0;
        for (int r = 0; r < width; r++) total += ranks[r];
        u *= total;
        for (int r = 0; r < width; r++) {
            if (u < ranks[r]) return r;
            u -= ranks[r];
        }

        return width - 1;
    }
}
