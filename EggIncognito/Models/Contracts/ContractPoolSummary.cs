namespace EggIncognito.Models.Contracts;

public sealed record ContractPoolSummary(
    ContractSlotKind Kind,
    int Candidates,
    double? GapDays,
    int GapSamples,
    double LengthDays,
    string Summary);
