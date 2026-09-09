using System.Globalization;
using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class DeviceMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IDeviceFleet fleet,
    DeviceConfig config,
    DeviceRecertConfig recertConfig,
    TimeProvider time,
    DeviceProxyPusher proxyPusher,
    IEnumerable<IDeviceStoreChecker> storeCheckers,
    IConfiguration appConfig,
    IosStoreCatalog catalog,
    AndroidStoreCatalog androidCatalog,
    KnownVersionRecorder knownVersions,
    DeviceClaimRegistry claims,
    ILogger<DeviceMaintenanceService> logger) : BackgroundService {
    private static readonly TimeSpan ClimbHarvestBackoff = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PublishRetryBackoff = TimeSpan.FromMinutes(10);
    private readonly bool _syncEnabled = appConfig.GetValue("DeviceSync:Enabled", false);
    private readonly bool _autoPublish = appConfig.GetValue("DeviceSync:AutoPublish", true);
    private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(config.HarvestIntervalMinutes);
    private readonly TimeSpan _refreshSettle = TimeSpan.FromSeconds(config.HarvestSettleSeconds);
    private readonly TimeSpan _noOpRetryBackoff =
        TimeSpan.FromMinutes(appConfig.GetValue("DeviceSync:RetryBackoffMinutes", 360));
    private readonly TimeSpan _storeProbeInterval =
        TimeSpan.FromMinutes(appConfig.GetValue("DeviceSync:StoreProbeIntervalMinutes", 360));

#pragma warning disable IDE0028
    private readonly Dictionary<string, (string Build, DateTimeOffset At)> _lastClimbHarvest =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (string StoreLatest, DateTimeOffset At)> _lastNoOpCheck =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastStoreProbe =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastRefreshHarvest =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastRecertProbe =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (string Build, DateTimeOffset At)> _lastPublishTry =
        new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!config.Enabled) {
            logger.LogInformation("device maintenance disabled");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, config.IntervalMinutes)), time);
        try {
            await RunCycleAsync(true, stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken)) await RunCycleAsync(false, stoppingToken);
        } catch (OperationCanceledException) {
            /* shutdown */
        }
    }

    private async Task RunCycleAsync(bool startup, CancellationToken ct) {
        try {
            if (startup) await StartupHarvestAsync(ct);
            else await RefreshCapturesAsync(ct);
            await StoreSyncAllAsync(ct);
            await RecertSyncAsync(ct);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "device maintenance: cycle failed, skipping to the next tick");
        }
    }

    private async Task StartupHarvestAsync(CancellationToken ct) {
        foreach (var d in await fleet.EnabledAsync(ct)) {
            if (claims.IsHeld(d.Id)) {
                logger.LogDebug("device {Id} held by remote bridge, skipping maintenance", d.Id);
                continue;
            }

            if (DeviceOrigins.IsVirtual(d.Origin)) {
                logger.LogDebug("device {Id} is virtual, skipping version maintenance", d.Id);
                continue;
            }

            try {
                var rinfo = await HarvestAsync(d, TimeSpan.FromSeconds(25), ct);
                logger.LogInformation("device capture: {Id} startup harvest -> {Cv}",
                    d.Id, rinfo?.ClientVersion is { } cv ? $"clientVersion {cv}" : "no rinfo (will retry on demand)");
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "device capture: {Id} startup harvest threw", d.Id);
            }
        }
    }

    internal async Task RefreshCapturesAsync(CancellationToken ct) {
        if (_refreshInterval <= TimeSpan.Zero) return;
        foreach (var d in await fleet.EnabledAsync(ct)) {
            if (claims.IsHeld(d.Id)) {
                logger.LogDebug("device {Id} held by remote bridge, skipping maintenance", d.Id);
                continue;
            }

            if (DeviceOrigins.IsVirtual(d.Origin)) {
                logger.LogDebug("device {Id} is virtual, skipping version maintenance", d.Id);
                continue;
            }

            if (_lastRefreshHarvest.TryGetValue(d.Id, out var last) && time.GetUtcNow() - last < _refreshInterval)
                continue;

            try {
                logger.LogInformation("device capture: {Id} scheduled capture refresh (every {Minutes} min)",
                    d.Id, _refreshInterval.TotalMinutes);
                var rinfo = await HarvestAsync(d, TimeSpan.FromSeconds(40), ct);
                logger.LogInformation("device capture: {Id} refresh harvest -> {Cv}",
                    d.Id, rinfo?.ClientVersion is { } cv ? $"clientVersion {cv}" : "no rinfo");
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "device capture: {Id} refresh harvest threw", d.Id);
            }
        }
    }

    private async Task<DeviceRinfo?> HarvestAsync(DeviceEntry d, TimeSpan timeout, CancellationToken ct) {
        _lastRefreshHarvest[d.Id] = time.GetUtcNow();
        var rinfo = await proxyPusher.ForceHarvestAsync(d, timeout, ct, _refreshSettle);
        if (rinfo?.ClientVersion is { } cv) await RecordClientVersionAsync(d.Id, cv, ct);
        return rinfo;
    }

    private async Task RecordClientVersionAsync(string deviceId, int clientVersion, CancellationToken ct) {
        try {
            using var scope = scopeFactory.CreateScope();
            if (scope.ServiceProvider.GetService(typeof(DeviceStateStore)) is DeviceStateStore states)
                await states.RecordClientVersionAsync(deviceId, clientVersion, ct);
        } catch (Exception ex) {
            logger.LogDebug(ex, "device capture: {Id} clientVersion persist failed", deviceId);
        }
    }

    internal async Task StoreSyncAllAsync(CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService(typeof(IDeviceStatusStore)) is not IDeviceStatusStore store) return;
        var db = (EggIncognitoDbContext)sp.GetRequiredService(typeof(EggIncognitoDbContext));
        var jobs = (DeviceJobStore)sp.GetRequiredService(typeof(DeviceJobStore));

        var devices = await fleet.EnabledAsync(ct);
        var latest = (await jobs.LatestPerDeviceAsync(DeviceJobKinds.Probe, ct))
            .GroupBy(p => p.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        if (_syncEnabled) await RefreshStoreCatalogAsync(devices, ct);

        foreach (var d in await store.EnabledDevicesAsync(ct)) {
            if (claims.IsHeld(d.Id)) {
                logger.LogDebug("device {Id} held by remote bridge, skipping maintenance", d.Id);
                continue;
            }

            if (DeviceOrigins.IsVirtual(d.Origin)) {
                logger.LogDebug("device {Id} is virtual, skipping version maintenance", d.Id);
                continue;
            }

            if (!latest.TryGetValue(d.Id, out var probe)) continue;
            if (DeviceStreamGate.IsHeld(d.Id)) {
                logger.LogDebug("device sync: {Id} has a live screen stream, skipping the store check", d.Id);
                continue;
            }

            try {
                await StoreSyncAsync(d, probe, jobs, db, ct);
            } catch (Exception ex) {
                logger.LogWarning(ex, "device sync: {Id} threw", d.Id);
            }
        }

        try {
            await proxyPusher.PushAllAsync(devices, ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "device capture: proxy push tick failed");
        }

        await HarvestClimbedDevicesAsync(devices, latest, sp, ct);
        await EnsureBinaryStoredAsync(sp, ct);
        await AutoPublishAsync(devices, sp, ct);
    }

    private async Task AutoPublishAsync(
        IReadOnlyList<DeviceEntry> devices, IServiceProvider sp, CancellationToken ct) {
        if (!_autoPublish) return;
        if (sp.GetService(typeof(DeviceRegistryPublisher)) is not DeviceRegistryPublisher publisher) return;
        if (sp.GetService(typeof(DeviceStateStore)) is not DeviceStateStore states) return;

        foreach (var d in devices) {
            if (DeviceOrigins.IsVirtual(d.Origin)) continue;
            try {
                var state = await states.GetAsync(d.Id, ct);
                if (state?.Build is not { Length: > 0 } build) continue;
                if (await publisher.InRegistryAsync(d.Platform, build, ct)) continue;

                if (_lastPublishTry.TryGetValue(d.Id, out var last)
                    && string.Equals(last.Build, build, StringComparison.Ordinal)
                    && time.GetUtcNow() - last.At < PublishRetryBackoff)
                    continue;

                _lastPublishTry[d.Id] = (build, time.GetUtcNow());
                var res = await publisher.PublishAsync(d.Id, "device-auto", true, ct);
                if (res.Outcome == PublishOutcome.Published) {
                    _lastPublishTry.Remove(d.Id);
                    logger.LogInformation(
                        "device auto-publish: {Id} {Plat} {Version} build {Build} published to the registry",
                        d.Id, d.Platform, res.AppVersion, res.Build);
                } else {
                    logger.LogInformation(
                        "device auto-publish: {Id} build {Build} not ready yet ({Outcome}): {Note}",
                        d.Id, build, res.Outcome, res.Error);
                }
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "device auto-publish: {Id} threw", d.Id);
            }
        }
    }

    private async Task RefreshStoreCatalogAsync(IReadOnlyList<DeviceEntry> devices, CancellationToken ct) {
        try {
            string appId = appConfig["DeviceUpdate:Ios:AppId"] ?? "993492744";
            string? country = appConfig["DeviceUpdate:Ios:LookupCountry"];
            string? storeLatest = await catalog.LatestVersionAsync(appId, country, ct);
            if (storeLatest is not null)
                await knownVersions.RecordAsync(Platforms.Ios, storeLatest, "itunes-lookup", ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "device sync: ios store catalog refresh threw");
        }

        try {
            string? package = devices
                .FirstOrDefault(d => Platforms.Matches(d.Platform, Platforms.Android)
                                     && !DeviceOrigins.IsVirtual(d.Origin))?.Package;
            if (package is null) return;
            string? playLatest = await androidCatalog.LatestVersionAsync(
                package, appConfig["DeviceUpdate:Android:LookupCountry"],
                appConfig["DeviceUpdate:Android:LookupLocale"] ?? "en", ct);
            if (playLatest is not null)
                await knownVersions.RecordAsync(Platforms.Android, playLatest, "play-scrape", ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "device sync: play store catalog refresh threw");
        }
    }

    private async Task HarvestClimbedDevicesAsync(
        IReadOnlyList<DeviceEntry> devices, Dictionary<string, DeviceJobRow> latest, IServiceProvider sp,
        CancellationToken ct) {
        foreach (var d in devices) {
            if (claims.IsHeld(d.Id)) {
                logger.LogDebug("device {Id} held by remote bridge, skipping maintenance", d.Id);
                continue;
            }

            if (DeviceOrigins.IsVirtual(d.Origin)) {
                logger.LogDebug("device {Id} is virtual, skipping version maintenance", d.Id);
                continue;
            }

            if (!latest.TryGetValue(d.Id, out var probe)) continue;
            if (probe.Reachable != true || string.IsNullOrEmpty(probe.Build)) continue;
            var harvested = proxyPusher.LastRinfo(d.Id);
            if (harvested is not null &&
                string.Equals(harvested.Build, probe.Build, StringComparison.Ordinal)) {
                continue;
            }

            if (_lastClimbHarvest.TryGetValue(d.Id, out var last)
                && string.Equals(last.Build, probe.Build, StringComparison.Ordinal)
                && time.GetUtcNow() - last.At < ClimbHarvestBackoff)
                continue;

            _lastClimbHarvest[d.Id] = (probe.Build, time.GetUtcNow());

            try {
                logger.LogInformation(
                    "device capture: {Id} installed build {Build} not yet harvested (had {Prev}); launching app for fresh capture",
                    d.Id, probe.Build, harvested?.Build ?? "none");
                var rinfo = await HarvestAsync(d, TimeSpan.FromSeconds(40), ct);
                await BackfillClientVersionAsync(sp, d, probe.Build, rinfo, ct);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "device capture: {Id} climb harvest threw", d.Id);
            }
        }
    }

    private async Task BackfillClientVersionAsync(
        IServiceProvider sp, DeviceEntry d, string build, DeviceRinfo? rinfo, CancellationToken ct) {
        if (rinfo?.ClientVersion is not { } cv) return;
        if (sp.GetService(typeof(ProtoRegistryStore)) is not ProtoRegistryStore registry) return;

        var res = await registry.UpdateMetadataAsync(d.Platform, build, null,
            cv.ToString(CultureInfo.InvariantCulture), null, ct: ct);
        logger.LogInformation("device capture: {Id} backfill clientVersion {Cv} onto {Plat} build {Build} -> {Res}",
            d.Id, cv, d.Platform, build, res);
    }

    private async Task EnsureBinaryStoredAsync(IServiceProvider sp, CancellationToken ct) {
        if (sp.GetService(typeof(GameBinaryProvider)) is not GameBinaryProvider binaries) return;
        try {
            foreach ((string platform, string status, string? version, string? note) in
                     await binaries.EnsureAllVersionsStoredAsync(ct)) {
                switch (status) {
                    case "pulled":
                        logger.LogInformation("binary store: {Platform} pulled and stored {Version}; {Note}", platform,
                            version, note);
                        break;
                    case "pull-failed":
                    case "store-error":
                        logger.LogWarning("binary store: {Platform} ensure {Version} failed ({Status}): {Note}",
                            platform, version, status, note);
                        break;
                    default:
                        logger.LogDebug("binary store: {Platform} {Status} {Version} {Note}", platform, status,
                            version ?? "?", note);
                        break;
                }
            }

            (bool staged, string? stageNote) = await binaries.EnsureIosBinaryStagedAsync(ct);
            logger.LogDebug("binary store: ios stash {Result} ({Note})", staged ? "staged" : "skipped", stageNote);
        } catch (Exception ex) {
            logger.LogWarning(ex, "binary store: ensure tick threw");
        }
    }

    private async Task StoreSyncAsync(
        Device d, DeviceJobRow probe,
        DeviceJobStore jobs, EggIncognitoDbContext db, CancellationToken ct) {
        if (!_syncEnabled) return;
        if (probe.Reachable != true || string.IsNullOrEmpty(probe.AppVersion)) return;

        string? storeLatest = await StoreAheadCheck.StoreLatestAsync(db, d.Platform, ct,
            crossPlatformHint: string.Equals(d.Platform, "android", StringComparison.OrdinalIgnoreCase));
        bool ahead = StoreAheadCheck.IsAhead(storeLatest, probe.AppVersion);
        if (ahead) {
            if (_lastNoOpCheck.TryGetValue(d.Id, out var noOp)
                && string.Equals(noOp.StoreLatest, storeLatest, StringComparison.Ordinal)
                && time.GetUtcNow() - noOp.At < _noOpRetryBackoff) {
                logger.LogDebug("device sync: {Id} store {Store} already checked recently (no-op); backing off",
                    d.Id, storeLatest);
                return;
            }
        } else {
            if (_lastStoreProbe.TryGetValue(d.Id, out var lastProbe)
                && time.GetUtcNow() - lastProbe < _storeProbeInterval)
                return;
        }

        var checker = storeCheckers.FirstOrDefault(c =>
            string.Equals(c.Platform, d.Platform, StringComparison.OrdinalIgnoreCase));
        if (checker is null) {
            logger.LogInformation("device sync: {Id} store {Store} > installed {Inst} but no {Plat} store checker",
                d.Id, storeLatest, probe.AppVersion, d.Platform);
            return;
        }

        if (ahead)
            logger.LogInformation("device sync: {Id} store {Store} > installed {Inst}: driving on-device store",
                d.Id, storeLatest, probe.AppVersion);
        else
            logger.LogInformation("device sync: {Id} periodic store probe (installed {Inst}, no known newer version)",
                d.Id, probe.AppVersion);

        _lastStoreProbe[d.Id] = time.GetUtcNow();
        var target = new DeviceTarget(d.Id, d.Platform, d.Target, d.Package);
        var result = await checker.CheckAndUpdateAsync(target, ct,
            msg => logger.LogInformation("device sync: {Id} {Msg}", d.Id, msg));

        if (result.Installed) {
            _lastNoOpCheck.Remove(d.Id);
            await jobs.RecordAsync(
                d.Id, DeviceJobKinds.StoreCheck, "heartbeat", "updated", result.Note,
                new DeviceJobFacts(
                    AppVersion: result.InstalledAfter,
                    Detail: new { fromVersion = result.InstalledBefore, toVersion = result.InstalledAfter }),
                ct);
        } else if (result.Action is "up_to_date" or "manual_needed" && storeLatest is not null)
            _lastNoOpCheck[d.Id] = (storeLatest, time.GetUtcNow());
    }

    internal async Task RecertSyncAsync(CancellationToken ct) {
        if (!recertConfig.Enabled) return;

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceRecertService)) is not DeviceRecertService recert) return;

        foreach (var d in await fleet.EnabledAsync(ct)) {
            if (claims.IsHeld(d.Id)) {
                logger.LogDebug("device {Id} held by remote bridge, skipping maintenance", d.Id);
                continue;
            }

            if (DeviceOrigins.IsVirtual(d.Origin)) {
                logger.LogDebug("device {Id} is virtual, skipping version maintenance", d.Id);
                continue;
            }

            if (!Platforms.Matches(d.Platform, Platforms.Android)) continue;
            if (_lastRecertProbe.TryGetValue(d.Id, out var last) && time.GetUtcNow() - last < _storeProbeInterval)
                continue;
            _lastRecertProbe[d.Id] = time.GetUtcNow();

            try {
                string? expiry = await recert.ReadExpiryAsync(d.Id, ct);
                if (expiry is null || !TryParseExpiryDays(expiry, time.GetUtcNow(), out int daysLeft)) continue;
                if (daysLeft > recertConfig.ExpiryWarnDays) continue;

                logger.LogInformation(
                    "device recert: {Id} expiry {Expiry} ({Days}d) within warn threshold {Warn}d; auto-recertifying",
                    d.Id, expiry, daysLeft, recertConfig.ExpiryWarnDays);
                var result = await recert.RecertAsync(d.Id, "auto", ct);
                logger.LogInformation("device recert: {Id} auto recert {Outcome}",
                    d.Id, result.Ok ? "ok" : $"failed ({result.FailedStep})");
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "device recert: {Id} auto path threw", d.Id);
            }
        }
    }

    private static bool TryParseExpiryDays(string value, DateTimeOffset now, out int days) {
        string trimmed = value.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) {
            days = n;
            return true;
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) {
            days = (int)Math.Floor((date - now).TotalDays);
            return true;
        }

        days = 0;
        return false;
    }
}
