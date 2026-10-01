using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions.Contracts;

public sealed record ContractPool(
    ContractSlotKind Kind,
    IReadOnlyList<ContractCandidate> Candidates,
    double? GapSeconds,
    int GapSamples,
    double LengthSeconds,
    RuleEvidence Evidence);
