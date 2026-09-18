using System.Collections.Concurrent;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Devices.Fake;

public sealed class FakeDeviceAgent(
    IServiceScopeFactory scopes,
    FakeDeviceSettings settings,
    IDevicePlatforms platforms,
    TimeProvider time,
    ILogger<FakeDeviceAgent> logger) : BackgroundService, IDeviceAgentClient {
    private const string Trigger = "fake-agent";

    private readonly ConcurrentDictionary<string, byte> _wedged = new(StringComparer.OrdinalIgnoreCase);

    public bool Enabled => true;

    public async Task<DeviceProbeDto?> ProbeAsync(string id, CancellationToken ct) {
        if (settings.For(id) is not { } fake) return null;

        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService(typeof(EggIncognitoDbContext)) is not EggIncognitoDbContext db) return null;
        if (sp.GetService(typeof(IDeviceStatusStore)) is not IDeviceStatusStore store) return null;
        if (sp.GetService(typeof(DeviceJobStore)) is not DeviceJobStore jobs) return null;

        var device = await store.GetAsync(fake.Id, ct) ?? Row(fake);
        var row = await DeviceProbeRunner.ProbeOneAsync(device, Trigger, platforms, jobs, db, logger, time, ct);
        string? latestAvailable = await db.KnownVersions.AsNoTracking()
            .Where(k => k.Platform == fake.Platform)
            .OrderByDescending(k => k.FirstSeen)
            .Select(k => k.AppVersion)
            .FirstOrDefaultAsync(ct);

        return new DeviceProbeDto(fake.Id, row.Reachable == true, row.AppVersion, row.Build, latestAvailable,
            row.Outcome ?? "", row.Message, row.StartedAt);
    }

    public async Task<int> ProbeAllAsync(CancellationToken ct) {
        int probed = 0;
        foreach (var fake in settings.Devices) {
            try {
                if (await ProbeAsync(fake.Id, ct) is not null) probed++;
            } catch (Exception ex) {
                logger.LogWarning(ex, "fake agent: probe of {Id} threw", fake.Id);
            }
        }

        return probed;
    }

    public Task<bool> PokeAsync(string? id, bool force, CancellationToken ct) {
        IReadOnlyList<FakeDevice> targets;
        if (string.IsNullOrEmpty(id)) {
            targets = settings.Devices;
        } else {
            targets = settings.For(id) is { } one ? [one] : [];
        }

        if (targets.Count == 0) return Task.FromResult(false);

        foreach (var fake in targets) {
            _ = Task.Run(() => PassAsync(fake, force, CancellationToken.None), CancellationToken.None);
        }

        return Task.FromResult(true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("fake device agent: {Count} fake device(s) declared: {Ids}",
            settings.Devices.Count, string.Join(", ", settings.Devices.Select(d => $"{d.Id} ({d.Scenario})")));

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, settings.SweepMinutes)), time);
        try {
            await ClearStuckHarvestsAsync(stoppingToken);
            await SweepAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken)) await SweepAsync(stoppingToken);
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "fake device agent: sweep loop stopped");
        }
    }

    private async Task ClearStuckHarvestsAsync(CancellationToken ct) {
        try {
            using var scope = scopes.CreateScope();
            if (scope.ServiceProvider.GetService(typeof(DeviceStateStore)) is not DeviceStateStore states) return;
            int cleared = await states.ResetRunningAsync(ct);
            if (cleared > 0) logger.LogInformation("fake agent: cleared {Count} interrupted harvest(s)", cleared);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "fake agent: clearing interrupted harvests threw");
        }
    }

    private async Task SweepAsync(CancellationToken ct) {
        foreach (var fake in settings.Devices) {
            try {
                await ProbeAsync(fake.Id, ct);
                await PassAsync(fake, false, ct);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "fake agent: sweep of {Id} threw", fake.Id);
            }
        }
    }

    private async Task PassAsync(FakeDevice fake, bool force, CancellationToken ct) {
        try {
            if (fake.Scenario == FakeScenarios.Wedged && _wedged.TryAdd(fake.Id, 0)) {
                await PlantWedgeAsync(fake, ct);
                return;
            }

            using var scope = scopes.CreateScope();
            var sp = scope.ServiceProvider;
            if (sp.GetService(typeof(DeviceStateStore)) is not DeviceStateStore states) return;
            if (sp.GetService(typeof(DeviceHarvester)) is not DeviceHarvester harvester) return;
            if (!await states.TryBeginAsync(fake.Id, ct)) {
                logger.LogInformation("fake agent: {Id} already harvesting, deferring", fake.Id);
                return;
            }

            var target = new DeviceTarget(fake.Id, fake.Platform, fake.Target, fake.Package);
            var outcome = await harvester.RunAsync(target, force, ct);
            logger.LogInformation("fake agent: {Id} harvest {Status} ({Note})", fake.Id, outcome.Status,
                outcome.Note ?? "");
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "fake agent: {Id} pass cancelled", fake.Id);
        } catch (Exception ex) {
            logger.LogWarning(ex, "fake agent: {Id} pass threw", fake.Id);
        }
    }

    private async Task PlantWedgeAsync(FakeDevice fake, CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService(typeof(EggIncognitoDbContext)) is not EggIncognitoDbContext db) return;

        var backdated = new BackdatedTimeProvider(time, TimeSpan.FromMinutes(settings.WedgeBackdateMinutes));
        var jobs = new DeviceJobStore(db, backdated, sp.GetService(typeof(IDeviceJobSink)) as IDeviceJobSink);
        var job = await jobs.TryStartAsync(fake.Id, DeviceJobKinds.Harvest, Trigger,
            "harvest wedged by the wedged scenario", ct);
        logger.LogInformation("fake agent: {Id} planted a stale {Kind} job backdated {Minutes} minutes ({State})",
            fake.Id, DeviceJobKinds.Harvest, settings.WedgeBackdateMinutes,
            job is null ? "refused, another job is running" : "running");
    }

    private static Device Row(FakeDevice fake) => new() {
        Id = fake.Id,
        Platform = fake.Platform,
        Label = fake.Label,
        Target = fake.Target,
        Package = fake.Package,
        Enabled = true
    };

    private sealed class BackdatedTimeProvider(TimeProvider inner, TimeSpan offset) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow() - offset;
    }
}
