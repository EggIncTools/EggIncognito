namespace EggIncognito.Core.Services.Devices;

public sealed record HostFacts(
    string Hostname,
    string? GitSha,
    string? Network,
    string? AdbPublicKey,
    string AdbSocket,
    bool AdbServerOwned,
    bool DockerPresent);

public interface IHostFacts {
    Task<DeviceResult<HostFacts>> GetAsync(CancellationToken ct);
}
