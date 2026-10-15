namespace EggIncognito.Models.Contracts;

public sealed record NextContractsResponse(
    double GeneratedAt, NextPick? NonPe, NextPick? Pe, NextPick? UltraPe, IReadOnlyList<NextPick> Colleggtibles);
