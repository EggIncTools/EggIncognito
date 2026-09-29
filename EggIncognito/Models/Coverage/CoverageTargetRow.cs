namespace EggIncognito.Models.Coverage;

public sealed record CoverageTargetRow(
    long Id,
    string? SpecName,
    string? Level,
    string? Rarity,
    int ItemTarget,
    int ObservationTarget,
    bool Enabled);
