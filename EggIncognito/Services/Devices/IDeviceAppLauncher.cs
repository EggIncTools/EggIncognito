using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public interface IDeviceAppLauncher {
    Task<DeviceCookbookRun> LaunchAsync(DeviceTarget target, Func<string, Task> progress, CancellationToken ct);
}
