namespace EggIncognito.Core.Services.Devices;

public sealed record BridgeFleetEntry(
    string Id,
    string Platform,
    string Label,
    string Target,
    string Package,
    string Origin,
    int? CapturePort);

public sealed record BridgeFleet(IReadOnlyList<BridgeFleetEntry> Devices, string? CaptureHostIp);

public sealed record BridgeClaimBody(int? TtlSeconds);

public sealed record BridgeClaimOutcome(bool Ok, DateTimeOffset ExpiresAt);
