using System.Collections.Concurrent;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using EggIncognito.Runner.Data;

namespace EggIncognito.Runner.Harvest;

public sealed class HarvestScheduler(RunnerDb db, IDevicePlatforms platforms, ILoggerFactory logs) {
    private readonly ConcurrentDictionary<string, HarvestLoop> _loops = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger = logs.CreateLogger("HarvestScheduler");

    public bool Busy(string deviceId) => _loops.TryGetValue(deviceId, out var loop) && loop.Running;

    public void Poke(string deviceId, bool force) {
        var loop = _loops.GetOrAdd(deviceId, _ => new HarvestLoop());
        loop.Poke(force, f => HarvestOnceAsync(deviceId, f),
            ex => _logger.LogError(ex, "harvest pass failed for {DeviceId}", deviceId));
    }

    public async Task PokeAllAsync(CancellationToken ct, bool force = false) {
        using var ctx = db.NewContext();
        var devices = await new DeviceStatusStore(ctx).EnabledDevicesAsync(ct);
        foreach (var d in devices) Poke(d.Id, force);
    }

    private async Task HarvestOnceAsync(string deviceId, bool force) {
        using var ctx = db.NewContext();
        var statuses = new DeviceStatusStore(ctx);
        var device = await statuses.GetAsync(deviceId, CancellationToken.None);
        if (device is null) {
            _logger.LogWarning("harvest: unknown device {DeviceId}", deviceId);
            return;
        }

        var states = new DeviceStateStore(ctx);
        if (!await states.TryBeginAsync(deviceId, CancellationToken.None)) {
            _logger.LogInformation("harvest: {DeviceId} already marked running in db, deferring", deviceId);
            return;
        }

        var harvester = new DeviceHarvester(platforms, new DeviceAssetStore(ctx, BlobBytes.Inline), states,
            new DeviceJobStore(ctx, TimeProvider.System), new GameBinaryStore(ctx, BlobBytes.Inline),
            new ApkStore(ctx, TimeProvider.System, BlobBytes.Inline), logs.CreateLogger<DeviceHarvester>());
        var target = new DeviceTarget(device.Id, device.Platform, device.Target, device.Package);
        await harvester.RunAsync(target, force, CancellationToken.None);
    }
}
