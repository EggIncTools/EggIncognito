namespace EggIncognito.Models.Devices;

public sealed record IslandIntegrityReport(
    string? VerifiedBootState,
    string? Fingerprint,
    IReadOnlyDictionary<string, bool> Modules,
    bool TrickyStore,
    string? KeyboxModified,
    IReadOnlyList<string> Targets,
    IReadOnlyDictionary<string, string> IslandPackages,
    string? IslandGsfId,
    string? OwnerGsfId) {
    public bool HasModule(params string[] needles) =>
        Modules.Any(m => m.Value && needles.Any(n => m.Key.Contains(n, StringComparison.OrdinalIgnoreCase)));

    public bool IsTargeted(string package) =>
        Targets.Any(t => t.Trim().TrimEnd('!', '?').Equals(package, StringComparison.Ordinal));
}
