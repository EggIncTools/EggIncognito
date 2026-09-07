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

public sealed record BridgeInstance(
    string InstanceId,
    string Kind,
    string Image,
    string State,
    string? AdbSerial,
    string? HostRef,
    DateTimeOffset CreatedAt,
    string? Note,
    string? DeviceId);

public sealed record BridgeInstanceCreate(string? Image);

public sealed record BridgeInstanceList(
    bool Ok, string? Outcome, string? Note, IReadOnlyList<BridgeInstance> Instances);

public sealed record BridgeInstanceResult(bool Ok, string? Outcome, string? Note, BridgeInstance? Instance);

public sealed record BridgeClaimBody(int? TtlSeconds);

public sealed record BridgeClaimOutcome(bool Ok, DateTimeOffset ExpiresAt);
