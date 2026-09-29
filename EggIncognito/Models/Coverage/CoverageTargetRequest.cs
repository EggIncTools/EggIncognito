using System.Text.RegularExpressions;

namespace EggIncognito.Models.Coverage;

public sealed partial record CoverageTargetRequest(
    string? SpecName,
    string? Level,
    string? Rarity,
    int ItemTarget,
    int ObservationTarget,
    bool Enabled) {
    [GeneratedRegex("^[A-Z][A-Z_]*$")]
    private static partial Regex EnumName();

    public string? Error() {
        if (ItemTarget < 1) return "itemTarget must be at least 1";
        if (ObservationTarget < 1) return "observationTarget must be at least 1";
        string?[] scope = [SpecName, Level, Rarity];
        return scope.Any(s => s is not null && !EnumName().IsMatch(s)) ? "scope values must be proto enum names" : null;
    }
}
