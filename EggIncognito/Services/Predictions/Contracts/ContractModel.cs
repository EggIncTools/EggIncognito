using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions.Contracts;

public sealed record ContractModel(
    double TrainedAt,
    ReleaseGrid Grid,
    IReadOnlyDictionary<ContractSlotKind, ContractPool> Pools,
    IReadOnlyDictionary<ContractSlotKind, IReadOnlyList<double>> Ranks) {
    public const int Paths = 1000;
    private const int Seed = 20261011;

    public static ContractModel Train(IReadOnlyList<ContractReleaseSample> samples, double asOf) =>
        new(asOf, ReleaseGrid.Fit(samples, asOf), OldestFirstPools.Fit(samples), QueueRanks.Fit(samples, asOf));

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
                var eligible = OldestFirstPools.Eligible(Pools[slot.Kind] with { Candidates = queues[slot.Kind] }, time);
                if (eligible.Count == 0) continue;
                var pick = eligible[SampleRank(Ranks[slot.Kind], eligible.Count, random.NextDouble())];
                tallies[i][pick.ContractId] = tallies[i].GetValueOrDefault(pick.ContractId) + 1;
                var released = pick with { LastReleased = time, Releases = pick.Releases + 1 };
                foreach (var kind in SharedPools(slot.Kind)) {
                    queues[kind].RemoveAll(c => c.ContractId == pick.ContractId);
                    queues[kind].Add(released);
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

    private static ContractSlotKind[] SharedPools(ContractSlotKind kind) =>
        kind is ContractSlotKind.PeLeggacy or ContractSlotKind.PeLeggacyUltra ? OldestFirstPools.PeKinds : [kind];

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
