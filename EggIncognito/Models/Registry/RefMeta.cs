namespace EggIncognito.Models.Registry;

public sealed record RefMeta(
    string Platform,
    string? AppVersion,
    string? Build,
    string? ClientVersion,
    string? Source,
    string? Package,
    string? Detected,
    string? Sha,
    bool? Archived = null);
