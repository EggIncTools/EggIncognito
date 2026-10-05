using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices;

public sealed class DeviceFleet(IServiceScopeFactory scopeFactory, DeviceConfig config, bool fromDb) : IDeviceFleet {
    public async Task<IReadOnlyList<DeviceEntry>> EnabledAsync(CancellationToken ct) {
        if (!fromDb) return config.Devices;

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(IDeviceStatusStore)) is not IDeviceStatusStore store)
            return config.Devices;

        var rows = await store.EnabledDevicesAsync(ct);
        return [.. rows.Select(Entry)];
    }

    public async Task PersistCapturePortAsync(string deviceId, int port, CancellationToken ct) {
        if (!fromDb) return;

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(IDeviceStatusStore)) is not IDeviceStatusStore store) return;

        await store.SetCapturePortAsync(deviceId, port, ct);
    }

    private static DeviceEntry Entry(Device d) =>
        new(d.Id, d.Platform, d.Label, d.Target, d.Package, d.Origin, d.CapturePort);
}
