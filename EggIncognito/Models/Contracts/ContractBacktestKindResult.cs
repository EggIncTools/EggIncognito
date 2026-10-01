namespace EggIncognito.Models.Contracts;

public sealed record ContractBacktestKindResult(
    ContractSlotKind Kind,
    int Predicted,
    int SlotHit,
    int Top1Hit,
    int Top5Hit);
