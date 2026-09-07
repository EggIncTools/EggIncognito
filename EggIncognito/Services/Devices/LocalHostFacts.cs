using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class LocalHostFacts(
    DockerEngineClient docker,
    VirtualDeviceConfig config,
    AdbServerHost adb) : IHostFacts {
    public async Task<DeviceResult<HostFacts>> GetAsync(CancellationToken ct) {
        return DeviceResult<HostFacts>.Success(new HostFacts(
            Environment.MachineName,
            Environment.GetEnvironmentVariable("GIT_SHA"),
            await NetworkAsync(ct),
            AdbHostKey.Resolve(config),
            adb.Socket,
            adb.Owned,
            docker.Available));
    }

    private async Task<string?> NetworkAsync(CancellationToken ct) {
        if (config.Network is { Length: > 0 } pinned) return pinned;
        var self = await docker.SelfNetworkAsync(ct);
        return self.Ok ? self.Value : null;
    }
}
