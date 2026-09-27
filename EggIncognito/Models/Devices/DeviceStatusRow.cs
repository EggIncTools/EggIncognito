namespace EggIncognito.Models.Devices;

public sealed record DeviceStatusRow(
    string Id,
    string Platform,
    string Label,
    string? Target,
    string? Package,
    bool Reachable,
    string? InstalledAppVersion,
    string? InstalledBuild,
    int? ClientVersion,
    string? StoreLatest,
    bool StoreAhead,
    string Result,
    string? Note,
    DateTimeOffset? ProbedAt,
    DeviceUpdateSummary? LastUpdate,
    int CapturePort = 0);
