namespace EggIncognito.Services.Devices;

public sealed record DeviceProbeDto(
    string Id, bool Reachable, string? InstalledAppVersion, string? InstalledBuild,
    string? LatestAvailable, string Result, string? Note, DateTimeOffset ProbedAt);

public interface IDeviceAgentClient {
    bool Enabled { get; }
    Task<DeviceProbeDto?> ProbeAsync(string id, CancellationToken ct);
    Task<int> ProbeAllAsync(CancellationToken ct);
    Task<bool> PokeAsync(string? id, bool force, CancellationToken ct);
}
