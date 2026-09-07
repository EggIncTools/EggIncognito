using EggIdentity.Settings.Store;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Admin;
using EggIncognito.Services.Config;

namespace EggIncognito.Services.Devices;

public sealed class VirtualDeviceLifecycle(
    IServiceScopeFactory scopeFactory,
    IDeviceProvisioners provisioners,
    VirtualDeviceConfig config,
    VirtualDeviceReadinessProbe readiness,
    IDeviceConnectionFactory connections,
    IProcessRunner runner,
    IAdbServer adbServer,
    AdminNotifier notifier,
    TimeProvider time,
    ILogger<VirtualDeviceLifecycle> logger) : BackgroundService {
    private const string Package = "com.auxbrain.egginc";
    private static readonly TimeSpan AdbTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BootstrapBackoff = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SeedStartTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SeedInstallTimeout = TimeSpan.FromMinutes(25);
    private static readonly TimeSpan CheckinBackoff = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _gate = new(1, 1);
#pragma warning disable IDE0028
    private readonly Dictionary<string, DateTimeOffset> _lastBootstrap = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastIntegrity = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _seedSeen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _adbServerRestarted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastCheckin = new(StringComparer.Ordinal);
#pragma warning restore IDE0028

    public bool Supported { get; private set; }
    public string? SupportNote { get; private set; }

    public IDeviceProvisioner Provisioner => provisioners.For(config.Kind);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!config.Enabled) {
            logger.LogInformation("virtual devices: disabled (Devices:Virtual:Enabled is false)");
            return;
        }

        var probe = await Provisioner.ListAsync(stoppingToken);
        Supported = probe.Ok;
        SupportNote = probe.Note;
        if (!probe.Ok) {
            logger.LogWarning("virtual devices: provisioner '{Kind}' is not usable ({Outcome}): {Note}",
                config.Kind, DeviceOutcomes.Label(probe.Outcome), probe.Note ?? "no detail");
        } else {
            logger.LogInformation("virtual devices: provisioner '{Kind}' ready, {Count} container(s) present",
                config.Kind, probe.Value?.Count ?? 0);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, config.ReconcileSeconds)), time);
        try {
            await ReconcileAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken)) await ReconcileAsync(stoppingToken);
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "virtual devices: reconcile loop cancelled");
        }
    }

    public async Task<DeviceResult<ProvisionedInstance>> CreateAsync(string? image, CancellationToken ct) {
        if (!config.Enabled) return DeviceResult<ProvisionedInstance>.Unsupported("virtual devices are disabled");

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(ProvisionedInstanceStore)) is not ProvisionedInstanceStore store)
            return DeviceResult<ProvisionedInstance>.Unsupported("no database configured");

        int live = await store.CountLiveAsync(ct);
        if (live >= config.MaxInstances) {
            return DeviceResult<ProvisionedInstance>.Error(
                $"virtual device cap reached ({live}/{config.MaxInstances}); destroy one before creating another");
        }

        string resolvedImage = await ResolveImageAsync(scope.ServiceProvider, image, ct);
        var created = await Provisioner.CreateAsync(new ProvisionSpec(config.Kind, resolvedImage), ct);
        if (!created.Ok || created.Value is not { } instance) return created;

        await store.AddAsync(instance, ct);
        return created;
    }

    public async Task<string> ResolveImageAsync(IServiceProvider sp, string? requested, CancellationToken ct) {
        if (!string.IsNullOrWhiteSpace(requested)) return requested;
        if (sp.GetService(typeof(SettingsStore)) is SettingsStore settings) {
            var active = await settings.GetAsync(SettingKeys.VirtualImageOverride, ct);
            if (!string.IsNullOrWhiteSpace(active?.Value)) return active.Value;
        }

        return config.Image;
    }

    public async Task<DeviceResult> DestroyAsync(string instanceId, CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService(typeof(ProvisionedInstanceStore)) is not ProvisionedInstanceStore store)
            return DeviceResult.Unsupported("no database configured");

        var row = await store.GetAsync(instanceId, ct);
        if (row is null) return DeviceResult.Error($"unknown virtual device '{instanceId}'");

        if (row.DeviceId is { Length: > 0 } deviceId
            && sp.GetService(typeof(DeviceJobStore)) is DeviceJobStore jobs) {
            var running = await jobs.RunningAsync(ct);
            if (running.FirstOrDefault(j => string.Equals(j.DeviceId, deviceId, StringComparison.Ordinal)) is { } job) {
                return DeviceResult.Error(
                    $"device job '{job.Kind}' is running on {deviceId}; destroy again once it finishes");
            }
        }

        var destroyed = await Provisioner.DestroyAsync(instanceId, ct);
        if (!destroyed.Ok) return destroyed;

        if (row.DeviceId is { Length: > 0 } id && sp.GetService(typeof(IDeviceStatusStore)) is IDeviceStatusStore ds)
            await ds.RemoveAsync(id, ct);
        await store.RemoveAsync(instanceId, ct);
        _lastBootstrap.Remove(instanceId);
        return DeviceResult.Success(destroyed.Note);
    }

    public async Task<int> ReconcileAsync(CancellationToken ct) {
        if (!config.Enabled) return 0;
        if (!await _gate.WaitAsync(TimeSpan.Zero, ct)) return 0;
        try {
            return await ReconcileCoreAsync(ct);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "virtual devices: reconcile pass failed, retrying next tick");
            return 0;
        } finally {
            _gate.Release();
        }
    }

    private async Task<int> ReconcileCoreAsync(CancellationToken ct) {
        var listed = await Provisioner.ListAsync(ct);
        Supported = listed.Ok;
        SupportNote = listed.Note;
        if (!listed.Ok || listed.Value is not { } containers) return 0;

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService(typeof(ProvisionedInstanceStore)) is not ProvisionedInstanceStore store) return 0;
        if (sp.GetService(typeof(IDeviceStatusStore)) is not IDeviceStatusStore devices) return 0;

        var byId = containers.ToDictionary(c => c.InstanceId, StringComparer.Ordinal);
        var changed = new List<string>();
        int touched = 0;

        foreach (var row in await store.ReconcilableAsync(ct)) {
            if (!byId.TryGetValue(row.InstanceId, out var container)) {
                if (row.State == ProvisionStates.Failed) continue;
                await SetStateAsync(store, changed, row, ProvisionStates.Failed, "container is gone", ct);
                logger.LogWarning("virtual devices: {Id} container vanished, marked failed", row.InstanceId);
                touched++;
                continue;
            }

            await store.TouchAsync(row.InstanceId, container.HostRef, container.AdbSerial, ct);
            touched++;

            if (container.State == ProvisionStates.Stopped) {
                if (row.State != ProvisionStates.Stopped)
                    await SetStateAsync(store, changed, row, ProvisionStates.Stopped, "container is not running", ct);
                continue;
            }

            if ((container.AdbSerial ?? row.AdbSerial) is not { Length: > 0 } serial) {
                if (row.State != ProvisionStates.Booting)
                    await SetStateAsync(store, changed, row, ProvisionStates.Booting,
                        "waiting for the container to get an address", ct);
                continue;
            }

            if (await BootCompletedAsync(serial, ct) is { Ok: false } boot) {
                if (boot.Detail is { } detail && AdbTcp.LooksUnauthorized(detail)) {
                    await UnauthorizedAsync(row, serial, store, changed, detail, ct);
                    continue;
                }

                string note = boot.Detail is { } d ? $"waiting for sys.boot_completed: {d}" : "waiting for sys.boot_completed";
                if (row.State is ProvisionStates.Creating or ProvisionStates.Booting)
                    await SetStateAsync(store, changed, row, ProvisionStates.Booting, note, ct);
                continue;
            }

            if (await SeedPendingAsync(row, serial, store, changed, ct)) continue;

            if (row.State != ProvisionStates.Ready)
                await SetStateAsync(store, changed, row, ProvisionStates.Ready, "android boot completed", ct);

            if (!await EnsureRootAsync(row, serial, store, changed, ct)) continue;

            string deviceId = await EnsureDeviceRowAsync(row, serial, devices, store, changed, ct);
            if (await EnsureAppInstalledAsync(row, deviceId, serial, store, changed, ct))
                await EnsureIntegrityAsync(row.InstanceId, deviceId, serial, ct);
        }

        if (changed.Count > 0) notifier.Publish(AdminTopics.VirtualDevices);
        return touched;
    }

    private static async Task SetStateAsync(ProvisionedInstanceStore store, List<string> changed,
        ProvisionedInstanceRow row, string state, string note, CancellationToken ct) {
        await store.SetStateAsync(row.InstanceId, state, note, ct);
        if (row.State != state || !string.Equals(row.Note, note, StringComparison.Ordinal)) changed.Add(row.InstanceId);
        row.State = state;
        row.Note = note ?? row.Note;
    }

    private async Task<string> EnsureDeviceRowAsync(
        ProvisionedInstanceRow row, string serial, IDeviceStatusStore devices, ProvisionedInstanceStore store,
        List<string> changed, CancellationToken ct) {
        string deviceId = row.DeviceId ?? row.InstanceId;
        var existing = await devices.GetAsync(deviceId, ct);
        if (existing is null || !existing.Enabled || existing.Target != serial) {
            await devices.UpsertDeviceAsync(deviceId, Platforms.Android, deviceId, serial, Package,
                DeviceOrigins.Virtual, ct);
            changed.Add(deviceId);
            logger.LogInformation("virtual devices: registered {Id} as an android device on {Serial}",
                deviceId, serial);
        }

        if (row.DeviceId is null) {
            await store.SetDeviceAsync(row.InstanceId, deviceId, ct);
            changed.Add(row.InstanceId);
        }

        return deviceId;
    }

    private async Task<bool> EnsureAppInstalledAsync(
        ProvisionedInstanceRow row, string deviceId, string serial, ProvisionedInstanceStore store,
        List<string> changed, CancellationToken ct) {
        string instanceId = row.InstanceId;
        var pm = await Adb(["-s", serial, "shell", $"pm path {Package}"], AdbTimeout, ct);
        if (pm.ExitCode == 0 && pm.Stdout.Contains("package:", StringComparison.Ordinal)) {
            _lastBootstrap.Remove(instanceId);
            return true;
        }

        if (_lastBootstrap.TryGetValue(instanceId, out var lastTry)
            && time.GetUtcNow() - lastTry < BootstrapBackoff)
            return false;
        _lastBootstrap[instanceId] = time.GetUtcNow();

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceCookbookRunner)) is not DeviceCookbookRunner cookbookRunner) {
            logger.LogWarning("virtual devices: {Id} cannot bring up egg inc, no cookbook runner (db disabled?)",
                instanceId);
            return false;
        }

        var run = await cookbookRunner.RunNowAsync(
            deviceId, new DeviceCookbookRequest(DeviceCookbookIds.BringUp, null), "auto:provision", ct);
        if (!run.Ok) {
            await SetStateAsync(store, changed, row, ProvisionStates.Failed,
                run.Failure ?? "bring-up failed", ct);
            return false;
        }

        await SetStateAsync(store, changed, row, ProvisionStates.Ready, run.Note ?? "egg inc brought up", ct);
        logger.LogInformation("virtual devices: {Id} brought up egg inc on {Device}", instanceId, deviceId);
        return true;
    }

    private async Task EnsureIntegrityAsync(
        string instanceId, string deviceId, string serial, CancellationToken ct) {
        if (!config.IntegrityEnabled) return;
        if (_lastIntegrity.TryGetValue(instanceId, out var lastTry)
            && time.GetUtcNow() - lastTry < BootstrapBackoff)
            return;

        var target = new DeviceTarget(deviceId, Platforms.Android, serial, Package);
        var (modulesLive, chain) = await readiness.ChainAsync(target, ct);
        if (modulesLive && chain is { Activated: true }) {
            _lastIntegrity.Remove(instanceId);
            await EnsureCheckinAsync(instanceId, target, ct);
            return;
        }

        _lastIntegrity[instanceId] = time.GetUtcNow();

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceCookbookRunner)) is not DeviceCookbookRunner cookbookRunner) {
            logger.LogWarning("virtual devices: {Id} cannot install integrity, no cookbook runner (db disabled?)",
                instanceId);
            return;
        }

        string cookbook = modulesLive ? DeviceCookbookIds.ActivateIntegrity : DeviceCookbookIds.InstallIntegrity;
        var run = await cookbookRunner.RunNowAsync(
            deviceId, new DeviceCookbookRequest(cookbook, null), "auto:integrity", ct);
        if (!run.Ok) {
            logger.LogWarning("virtual devices: {Id} {Cookbook} failed: {Note}", instanceId, cookbook,
                run.Failure ?? "no detail");
            return;
        }

        logger.LogInformation("virtual devices: {Id} ran {Cookbook} on {Device}", instanceId, cookbook, deviceId);
    }

    private async Task EnsureCheckinAsync(string instanceId, DeviceTarget target, CancellationToken ct) {
        if (connections.For(target) is not { } conn) return;
        var root = await DeviceRoot.ProbeAsync(conn, ct);
        if (await GsfIdentity.ReadAsync(conn, root, ct) is not null) {
            _lastCheckin.Remove(instanceId);
            return;
        }

        if (_lastCheckin.TryGetValue(instanceId, out var last) && time.GetUtcNow() - last < CheckinBackoff) return;
        _lastCheckin[instanceId] = time.GetUtcNow();
        bool kicked = await GsfIdentity.KickAsync(conn, root, ct);
        logger.LogInformation("virtual devices: {Id} chain live but gms has no gsf id yet; checkin broadcast {Outcome}",
            instanceId, kicked ? "sent" : "did not run");
    }

    private async Task<bool> EnsureRootAsync(
        ProvisionedInstanceRow row, string serial, ProvisionedInstanceStore store, List<string> changed,
        CancellationToken ct) {
        var target = new DeviceTarget(row.DeviceId ?? row.InstanceId, Platforms.Android, serial, Package);
        if (connections.For(target) is not { } conn) {
            await SetStateAsync(store, changed, row, ProvisionStates.Failed, "no connection for this device", ct);
            return false;
        }

        var root = await DeviceRoot.EnsureAsync(conn, runner, serial, ct);
        if (root.Ok) return true;

        await SetStateAsync(store, changed, row, ProvisionStates.Failed,
            $"no root: {root.Detail} (needs adb root before the integrity chain, or Magisk su granted to uid 2000)", ct);
        return false;
    }

    private async Task<bool> SeedPendingAsync(
        ProvisionedInstanceRow row, string serial, ProvisionedInstanceStore store, List<string> changed,
        CancellationToken ct) {
        var probe = await Adb(["-s", serial, "shell", IntegritySeed.ProbeCommand], AdbTimeout, ct);
        var seed = IntegritySeed.Parse(probe.Stdout);
        if (!seed.Ran) {
            string why = DeviceParsing.TrimNote(probe.Stderr + probe.Stdout);
            await SetStateAsync(store, changed, row, ProvisionStates.Booting,
                $"seed probe did not run over adb (exit {probe.ExitCode}): {(why.Length > 0 ? why : "no output")}", ct);
            return true;
        }

        if (!seed.SeededImage) {
            _seedSeen.Remove(row.InstanceId);
            if (!ImageBuildSpec.LooksSeeded(row.Image)) return false;
            if (row.State != ProvisionStates.Failed) {
                await SetStateAsync(store, changed, row, ProvisionStates.Failed,
                    $"image {row.Image} is tagged {ImageBuildSpec.IntegrityToken} but {IntegritySeed.SeedScript} is absent; "
                    + "the seed layer did not land in the image", ct);
            }

            return true;
        }

        if (seed.State == IntegritySeed.StateDone) {
            _seedSeen.Remove(row.InstanceId);
            return false;
        }

        var now = time.GetUtcNow();
        if (!_seedSeen.TryGetValue(row.InstanceId, out var firstSeen)) {
            firstSeen = now;
            _seedSeen[row.InstanceId] = now;
        }

        var waited = now - firstSeen;
        string log = seed.LastLog is { } last ? $"; last log line: {DeviceParsing.TrimNote(last)}" : "";
        string? failure = seed.State switch {
            IntegritySeed.StateFailed => $"first-boot seed failed{log}; full log at {IntegritySeed.LogFile}",
            null when seed.Service == IntegritySeed.ServiceStopped =>
                $"first-boot seed exited without writing {IntegritySeed.StateFile}; "
                + $"init started {IntegritySeed.ServiceName} but the script died at once{log}",
            null when waited > SeedStartTimeout =>
                $"first-boot seed never wrote {IntegritySeed.StateFile} within {SeedStartTimeout.TotalMinutes:F0} min; "
                + $"{IntegritySeed.ServiceProp} is {seed.Service ?? "unset"}, "
                + (seed.Service is null ? $"init never loaded {IntegritySeed.RcFile}" : "the script is not writing state"),
            _ when waited > SeedInstallTimeout =>
                $"first-boot seed still {seed.State} after {SeedInstallTimeout.TotalMinutes:F0} min{log}",
            _ => null
        };

        if (failure is not null) {
            if (row.State != ProvisionStates.Failed) await SetStateAsync(store, changed, row, ProvisionStates.Failed, failure, ct);
            return true;
        }

        string phase = seed.State is null
            ? $"first-boot seed starting ({waited.TotalSeconds:F0}s, {IntegritySeed.ServiceProp}={seed.Service ?? "unset"})"
            : $"first-boot seed installing the integrity chain ({waited.TotalSeconds:F0}s), reboots when done";
        string note = seed.LastLog is { } line ? $"{phase}: {DeviceParsing.TrimNote(line)}" : phase;
        if (row.State != ProvisionStates.Booting || !string.Equals(row.Note, note, StringComparison.Ordinal))
            await SetStateAsync(store, changed, row, ProvisionStates.Booting, note, ct);
        return true;
    }

    private async Task UnauthorizedAsync(
        ProvisionedInstanceRow row, string serial, ProvisionedInstanceStore store, List<string> changed,
        string detail, CancellationToken ct) {
        var key = AdbHostKey.ResolveWithSource(config);
        string held = key is { } k ? $"this app holds {AdbHostKey.Label(k.Key)} from {k.Source}" : "this app found no adb public key at all";
        if (_adbServerRestarted.Add(row.InstanceId)) {
            logger.LogWarning(
                "virtual devices: {Id} adbd rejected the adb server's key ({Detail}); restarting the adb server from this "
                + "process so its key is the one this app bakes ({Held})", row.InstanceId, detail, held);
            await adbServer.RestartAsync(ct);
            await AdbTcp.ReviveAsync(runner, serial, ct);
            await SetStateAsync(store, changed, row, ProvisionStates.Booting,
                $"adbd rejected the adb server's key; {adbServer.Describe()}, {held}; retrying", ct);
            return;
        }

        if (row.State != ProvisionStates.Failed) {
            await SetStateAsync(store, changed, row, ProvisionStates.Failed,
                $"adbd rejects this host ({detail}); {adbServer.Describe()}, {held}. "
                + "The image was built with a different key, or with none; rebuild it", ct);
        }
    }

    private async Task<(bool Ok, string? Detail)> BootCompletedAsync(string serial, CancellationToken ct) {
        await Adb(["connect", serial], AdbTimeout, ct);
        var boot = await Adb(["-s", serial, "shell", "getprop sys.boot_completed"], AdbTimeout, ct);
        if (boot.ExitCode != 0 && AdbTcp.LooksDisconnected(boot.Stderr)) {
            await AdbTcp.ReviveAsync(runner, serial, ct);
            boot = await Adb(["-s", serial, "shell", "getprop sys.boot_completed"], AdbTimeout, ct);
        }

        if (boot.ExitCode == 0 && boot.Stdout.Trim() == "1") return (true, null);
        string detail = DeviceParsing.TrimNote(boot.Stderr + boot.Stdout);
        return (false, detail.Length > 0 ? detail : null);
    }

    private async Task<ProcessResult> Adb(string[] args, TimeSpan timeout, CancellationToken ct) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return await runner.RunAsync("adb", args, cts.Token);
    }

    public override void Dispose() {
        base.Dispose();
        _gate.Dispose();
    }
}
