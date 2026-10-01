using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions.Contracts;

public sealed record ContractModel(
    double TrainedAt,
    ReleaseGrid Grid,
    IReadOnlyDictionary<ContractSlotKind, ContractPool> Pools) {
    public static ContractModel Train(IReadOnlyList<ContractReleaseSample> samples, double asOf) =>
        new(asOf, ReleaseGrid.Fit(samples, asOf), OldestFirstPools.Fit(samples));

    public IReadOnlyList<ContractSlotPrediction> Slots(DateTimeOffset from, int horizonSlots, double now) {
        var consumed = new Dictionary<ContractSlotKind, int>();
        var slots = new List<ContractSlotPrediction>();
        foreach (var (time, slot) in Grid.Next(from, horizonSlots)) {
            var pool = Pools[slot.Kind];
            int skip = consumed.GetValueOrDefault(slot.Kind);
            consumed[slot.Kind] = skip + 1;
            slots.Add(new ContractSlotPrediction(
                time, slot.Kind, pool.LengthSeconds,
                slot.Evidence.Summary + ". " + pool.Evidence.Summary,
                OldestFirstPools.Top(pool, now, skip)));
        }

        return slots;
    }
}
