namespace EggIncognito.Models.Devices;

public sealed record DeviceRow(
    string Id,
    string Platform,
    string Label,
    string? Target,
    string? Package,
    bool Reachable,
    string? InstalledAppVersion,
    string? InstalledBuild,
    string? StoreLatest,
    bool StoreAhead,
    string Result,
    string? Note,
    DateTimeOffset? ProbedAt,
    int? ClientVersion,
    int CapturePort = 0);
