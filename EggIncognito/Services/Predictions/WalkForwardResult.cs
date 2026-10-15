namespace EggIncognito.Services.Predictions;

public sealed record WalkForwardKindResult(string Kind, int Slots, int Top1, int Top3, int Top5, double LogLoss);

public sealed record WalkForwardResult(
    double From,
    double To,
    double LagSeconds,
    IReadOnlyList<WalkForwardKindResult> Kinds,
    WalkForwardKindResult Total,
    int Uncovered);
