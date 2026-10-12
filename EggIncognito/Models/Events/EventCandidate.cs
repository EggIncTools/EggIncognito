namespace EggIncognito.Models.Events;

public sealed record EventCandidate(string Type, double Probability) {
    public IReadOnlyList<EventMagnitudeOdds> Magnitudes { get; init; } = [];
}
