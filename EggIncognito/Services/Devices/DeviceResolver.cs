using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices;

public readonly record struct DeviceQuery(string? DeviceId = null, string? Platform = null);

public interface IDeviceResolver {
    Task<Device?> ResolveAsync(DeviceQuery query, CancellationToken ct);
}

public sealed class DeviceResolver(IDeviceStatusStore? store = null, DeviceJobStore? jobs = null) : IDeviceResolver {
    public async Task<Device?> ResolveAsync(DeviceQuery query, CancellationToken ct) {
        if (store is null) return null;

        if (!string.IsNullOrEmpty(query.DeviceId) && await store.GetAsync(query.DeviceId, ct) is { } byId)
            return byId;

        var devices = await store.EnabledDevicesAsync(ct);
        if (!string.IsNullOrEmpty(query.Platform))
            devices = [.. devices.Where(d => Platforms.Matches(d.Platform, query.Platform))];
        if (devices.Count == 0) return null;

        if (jobs is null) return devices[0];
        var latest = (await jobs.LatestPerDeviceAsync(DeviceJobKinds.Probe, ct)).ToDictionary(p => p.DeviceId);
        var reachable = devices.Find(d => latest.TryGetValue(d.Id, out var p) && p.Reachable == true);
        return reachable ?? devices[0];
    }
}
