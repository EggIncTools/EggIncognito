using System.Collections.Concurrent;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Devices;

public sealed class LocalDeviceAgent(
    IServiceScopeFactory scopes,
    DeviceConfig config,
    IDevicePlatforms platforms,
    DeviceClaimRegistry claims,
    TimeProvider time,
    ILogger<LocalDeviceAgent> logger) : BackgroundService, IDeviceAgentClient {
    private const string Trigger = "poll";

    private readonly ConcurrentDictionary<string, HarvestLoop> _loops = new(StringComparer.OrdinalIgnoreCase);

    public bool Enabled => true;

    public bool Busy(string deviceId) => _loops.TryGetValue(deviceId, out var loop) && loop.Running;

    public async Task<DeviceProbeDto?> ProbeAsync(string id, CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService<EggIncognitoDbContext>() is not { } db) return null;
        if (sp.GetService<IDeviceStatusStore>() is not { } store) return null;
        if (sp.GetService<DeviceJobStore>() is not { } jobs) return null;

        var device = await store.GetAsync(id, ct);
        if (device is null) return null;

        var row = await DeviceProbeRunner.ProbeOneAsync(device, Trigger, platforms, jobs, db, logger, time, ct);
        string? latestAvailable = await db.KnownVersions.AsNoTracking()
            .Where(k => k.Platform == device.Platform)
            .OrderByDescending(k => k.FirstSeen)
            .Select(k => k.AppVersion)
            .FirstOrDefaultAsync(ct);

        return new DeviceProbeDto(device.Id, row.Reachable == true, row.AppVersion, row.Build, latestAvailable,
            row.Outcome ?? "", row.Message, row.StartedAt);
    }

    public async Task<int> ProbeAllAsync(CancellationToken ct) {
        int probed = 0;
        foreach (var device in await EnabledAsync(ct)) {
            if (claims.IsHeld(device.Id)) continue;
            try {
                if (await ProbeAsync(device.Id, ct) is not null) probed++;
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "device agent: probe of {Id} threw", device.Id);
            }
        }

        return probed;
    }

    public async Task<bool> PokeAsync(string? id, bool force, CancellationToken ct) {
        if (!string.IsNullOrEmpty(id)) {
            Poke(id, force);
            return true;
        }

        var devices = await EnabledAsync(ct);
        foreach (var device in devices) Poke(device.Id, force);
        return devices.Count > 0;
    }

    private void Poke(string deviceId, bool force) {
        var loop = _loops.GetOrAdd(deviceId, _ => new HarvestLoop());
        loop.Poke(force, f => HarvestOnceAsync(deviceId, f),
            ex => logger.LogError(ex, "harvest pass failed for {DeviceId}", deviceId));
    }

    private async Task HarvestOnceAsync(string deviceId, bool force) {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService<IDeviceStatusStore>() is not { } store) return;
        if (sp.GetService<DeviceStateStore>() is not { } states) return;
        if (sp.GetService<DeviceHarvester>() is not { } harvester) return;

        var device = await store.GetAsync(deviceId, CancellationToken.None);
        if (device is null) {
            logger.LogWarning("harvest: unknown device {DeviceId}", deviceId);
            return;
        }

        if (!await states.TryBeginAsync(deviceId, CancellationToken.None)) {
            logger.LogInformation("harvest: {DeviceId} already marked running, deferring", deviceId);
            return;
        }

        var target = new DeviceTarget(device.Id, device.Platform, device.Target, device.Package);
        var outcome = await harvester.RunAsync(target, force, CancellationToken.None);
        logger.LogInformation("harvest {DeviceId}: {Status} ({Note})", deviceId, outcome.Status, outcome.Note ?? "");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!config.Enabled) {
            logger.LogInformation("device agent disabled");
            return;
        }

        await ClearInterruptedAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, config.IntervalMinutes)), time);
        try {
            await SweepAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken)) await SweepAsync(stoppingToken);
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "device agent: sweep loop stopped");
        }
    }

    private async Task ClearInterruptedAsync(CancellationToken ct) {
        try {
            using var scope = scopes.CreateScope();
            if (scope.ServiceProvider.GetService<DeviceStateStore>() is not { } states) return;
            int cleared = await states.ResetRunningAsync(ct);
            if (cleared > 0) logger.LogInformation("device agent: cleared {Count} interrupted harvest(s)", cleared);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "device agent: clearing interrupted harvests threw");
        }
    }

    private async Task SweepAsync(CancellationToken ct) {
        try {
            await ProbeAllAsync(ct);
            await PokeAsync(null, false, ct);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "device agent: sweep threw");
        }
    }

    private async Task<IReadOnlyList<Device>> EnabledAsync(CancellationToken ct) {
        using var scope = scopes.CreateScope();
        if (scope.ServiceProvider.GetService<IDeviceStatusStore>() is not { } store) return [];
        return await store.EnabledDevicesAsync(ct);
    }
}
