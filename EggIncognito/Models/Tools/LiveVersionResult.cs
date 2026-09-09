namespace EggIncognito.Models.Tools;

public sealed record LiveVersionResult(
    bool Found,
    string? Platform = null,
    string? Version = null,
    string? Build = null,
    int? ClientVersion = null,
    string? LastSeen = null);
