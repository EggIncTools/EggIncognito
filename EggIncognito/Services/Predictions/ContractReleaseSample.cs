using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions;

public readonly record struct ContractReleaseSample(
    string ContractId,
    string Name,
    double Start,
    double LengthSeconds,
    bool Leggacy,
    int ProphecyEggs,
    bool UltraOnly) {
    public ContractSlotKind ReleaseKind {
        get {
            if (!Leggacy) return ContractSlotKind.NewContract;
            if (ProphecyEggs <= 0) return ContractSlotKind.Leggacy;
            return UltraOnly ? ContractSlotKind.PeLeggacyUltra : ContractSlotKind.PeLeggacy;
        }
    }
}
