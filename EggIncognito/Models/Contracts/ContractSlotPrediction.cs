namespace EggIncognito.Models.Contracts;

public sealed record ContractSlotPrediction(
    double SlotTime,
    ContractSlotKind Kind,
    double LengthSeconds,
    string Evidence,
    IReadOnlyList<ContractCandidate> Candidates);
