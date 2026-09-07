namespace EggIncognito.Core.Services.Devices;

public interface IDeviceConnectionFactory {
    IDeviceConnection? For(DeviceTarget target);
    SshDeviceConnection? Ios(string? hostFallback = null);
}

public sealed class DeviceConnectionFactory(IProcessRunner runner, DeviceCaptureConfig config)
    : IDeviceConnectionFactory {
    public IDeviceConnection? For(DeviceTarget target) => target.Platform?.ToLowerInvariant() switch {
        Platforms.Android => new AdbDeviceConnection(runner, target.Target),
        Platforms.Ios => Ios(target.Target),
        _ => null
    };

    public SshDeviceConnection? Ios(string? hostFallback = null) {
        string? host = config.IosSshHost ?? hostFallback;
        return string.IsNullOrEmpty(host) || string.IsNullOrEmpty(config.IosSshKeyPath)
            ? null
            : new SshDeviceConnection(runner, new SshEndpoint(host, config.IosSshPort, config.IosSshKeyPath));
    }
}
