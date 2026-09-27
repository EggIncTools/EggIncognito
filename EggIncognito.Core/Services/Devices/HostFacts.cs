namespace EggIncognito.Core.Services.Devices;

public sealed record HostFacts(
    string Hostname,
    string? GitSha,
    string AdbSocket,
    bool AdbServerOwned,
    string? CaptureHostIp = null,
    string? CaptureCaPem = null);

public interface IHostFacts {
    Task<DeviceResult<HostFacts>> GetAsync(CancellationToken ct);
}
