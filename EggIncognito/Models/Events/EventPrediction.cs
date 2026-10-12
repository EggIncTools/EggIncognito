namespace EggIncognito.Models.Events;

public sealed record EventPrediction(
    string? Type,
    bool Ultra,
    EventRuleKind Kind,
    string Rule,
    string Evidence,
    double PredictedStart,
    double PredictedEnd,
    double Confidence,
    IReadOnlyList<EventCandidate> Candidates,
    int Observed,
    int Expected,
    int PeriodDays,
    double LastStart) {
    public IReadOnlyList<EventMagnitudeOdds> Magnitudes { get; init; } = [];

    public double Chance(EventCandidate candidate) =>
        Kind is EventRuleKind.Pool or EventRuleKind.Ultra ? candidate.Probability : Confidence * candidate.Probability;
}
