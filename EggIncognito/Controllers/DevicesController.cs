using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EggIdentity.Contract;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/devices")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("read")]
public sealed partial class DevicesController(
    ICurrentUser currentUser,
    IServiceProvider services,
    IServiceScopeFactory scopeFactory) : ControllerBase {
    private const string StreamBoundary = "egiframe";
    private const int MinStreamFps = 1;
    private const int MaxStreamFps = 5;
    private const int MinVideoBitrate = 500_000;
    private const int MaxVideoBitrate = 8_000_000;

    [GeneratedRegex(@"^\d{2,4}x\d{2,4}$")]
    private static partial Regex VideoSizeRegex();

    private static readonly byte[] PartTrailer = "\r\n"u8.ToArray();
    private static readonly byte[] StreamEnd = Encoding.ASCII.GetBytes($"--{StreamBoundary}--\r\n");

    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;
    private DeviceJobStore? Jobs => services.GetService(typeof(DeviceJobStore)) as DeviceJobStore;
    private DeviceTimelineCache? Timeline => services.GetService(typeof(DeviceTimelineCache)) as DeviceTimelineCache;
    private DeviceJobFeed? JobFeed => services.GetService(typeof(DeviceJobFeed)) as DeviceJobFeed;
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    private ObjectResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin role required" });

    private static async Task<DeviceEntry?> FleetEntryAsync(IDeviceFleet fleet, string id, CancellationToken ct) =>
        (await fleet.EnabledAsync(ct)).FirstOrDefault(d => d.Id == id);

    private async Task<string?> ResolveDeviceIdAsync(string incoming, CancellationToken ct) {
        if (currentUser.IsAtLeast(UserRole.Admin)) return incoming;
        var store = Store;
        if (store is null) return null;
        var enabled = await store.EnabledDevicesAsync(ct);
        return enabled.FirstOrDefault(d => DevicePublicKey.For(d.Id) == incoming)?.Id;
    }

    [HttpGet("status")]
    [EnableRateLimiting("fetch")]
    public async Task<IActionResult> Status() {
        if (services.GetService(typeof(IDeviceFleet)) is not IDeviceFleet fleet || Timeline is not { } timeline)
            return Ok(Array.Empty<DeviceStatusRow>());

        var ct = HttpContext.RequestAborted;
        bool isAdmin = currentUser.IsAtLeast(UserRole.Admin);
        var enabled = (await fleet.EnabledAsync(ct))
            .Where(d => isAdmin || !DeviceOrigins.IsVirtual(d.Origin))
            .Select(AsDevice);

        var devices = enabled.ToDictionary(d => d.Id);
        var virtualUp = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        HashSet<string> virtualLive = isAdmin
            ? await MergeVirtualDevicesAsync(virtualUp, ct)
            : [with(StringComparer.Ordinal)];

        var ids = devices.Keys.ToList();
        var probes = (await timeline.LatestPerDeviceAsync(ids, DeviceJobKinds.Probe, ct))
            .ToDictionary(p => p.DeviceId, StringComparer.Ordinal);
        var updates = (await timeline.LatestPerDeviceAsync(ids, DeviceJobKinds.StoreCheck, ct))
            .ToDictionary(u => u.DeviceId, StringComparer.Ordinal);

        var platforms = devices.Values.Select(d => d.Platform).Distinct().ToList();
        var db = Db;
        var versions = db is null
            ? DeviceVersionIndex.Empty
            : await DeviceVersionIndex.BuildAsync(db, platforms, ct);
        var storeLatest = await StoreLatestPerPlatformAsync(db, platforms, ct);

        var captures = services.GetService(typeof(DeviceCaptureManager)) as DeviceCaptureManager;
        int capturePortFor(string id) =>
            captures?.PortFor(id) is > 0 and var listening ? listening : devices[id].CapturePort ?? 0;
        var inputs = new DeviceStatusInputs(
            isAdmin, probes, updates, storeLatest, virtualLive, versions,
            await CapturedClientVersionsAsync(ct),
            services.GetService(typeof(GameBinaryProvider)) as GameBinaryProvider,
            virtualUp, capturePortFor);

        return Ok(devices.Values.Select(d => DeviceStatusProjector.Project(d, inputs)));
    }

    private async Task<HashSet<string>> MergeVirtualDevicesAsync(Dictionary<string, DateTimeOffset> up,
        CancellationToken ct) {
        var live = new HashSet<string>(StringComparer.Ordinal);
        if (services.GetService(typeof(VirtualDeviceLifecycle)) is VirtualDeviceLifecycle { Delegated: true } lifecycle) {
            var listed = await lifecycle.Provisioner.ListAsync(ct);
            foreach (var instance in listed.Value ?? []) {
                if (instance.DeviceId is { Length: > 0 } deviceId) up[deviceId] = instance.CreatedAt;
            }

            return live;
        }

        if (Instances is not { } instances) return live;
        foreach (var row in await instances.AllAsync(ct)) {
            if (row.DeviceId is { Length: > 0 } deviceId) up[deviceId] = row.CreatedAt;
        }

        return live;
    }

    private static Device AsDevice(DeviceEntry e) => new() {
        Id = e.Id,
        Platform = e.Platform,
        Label = e.Label,
        Target = e.Target,
        Package = e.Package,
        Origin = e.Origin,
        CapturePort = e.CapturePort,
        Enabled = true
    };

    private static async Task<Dictionary<string, string?>> StoreLatestPerPlatformAsync(EggIncognitoDbContext? db,
        IEnumerable<string> platforms, CancellationToken ct) {
        var latest = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (db is null) return latest;

        foreach (string platform in platforms)
            latest[platform] = await StoreAheadCheck.StoreLatestAsync(db, platform, ct);
        return latest;
    }

    private async Task<Dictionary<string, int>> CapturedClientVersionsAsync(CancellationToken ct) {
        var captured = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (services.GetService(typeof(DeviceStateStore)) is not DeviceStateStore deviceStates) return captured;

        foreach (var s in await deviceStates.ListAsync(ct)) {
            if (s.ClientVersion is { } clientVersion) captured[s.DeviceId] = clientVersion;
        }

        return captured;
    }

    [HttpGet("{id}/jobs")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> JobHistory(string id, [FromQuery] int take = JobGroupCollapser.DefaultTake,
        [FromQuery] long? before = null, CancellationToken ct = default) {
        if (RequireAdmin() is { } no) return no;
        if (JobFeed is not { } feed) return StatusCode(503, new { error = "no database configured" });

        return Ok(await feed.PageAsync(id, take, before, ct));
    }

    [HttpGet("{id}/jobs/{jobId:long}/lines")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> JobLines(string id, long jobId, CancellationToken ct = default) {
        if (RequireAdmin() is { } no) return no;
        if (JobFeed is not { } feed) return StatusCode(503, new { error = "no database configured" });

        return Ok(await feed.LinesAsync(id, jobId, ct));
    }

    [HttpGet("jobs/live")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("fetch")]
    public async Task<IActionResult> LiveJobs(CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (JobFeed is not { } feed) return Ok(Array.Empty<LiveJob>());

        return Ok(await feed.LiveAsync(ct));
    }

    [HttpPost("{id}/refresh")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Refresh(string id) {
        if (RequireAdmin() is { } no) return no;

        if (services.GetService(typeof(IDeviceAgentClient)) is IDeviceAgentClient agent && agent.Enabled) {
            var dto = await agent.ProbeAsync(id, HttpContext.RequestAborted);
            if (dto is not null) {
                var agentDevice = await (Store?.GetAsync(id, HttpContext.RequestAborted) ?? Task.FromResult<Device?>(null));
                if (agentDevice is null) return NotFound(new { error = "unknown device" });
                return Ok(new {
                    id = dto.Id,
                    platform = agentDevice.Platform,
                    label = agentDevice.Label,
                    reachable = dto.Reachable,
                    installedAppVersion = dto.InstalledAppVersion,
                    installedBuild = dto.InstalledBuild,
                    latestAvailable = dto.LatestAvailable,
                    result = dto.Result,
                    note = dto.Note,
                    probedAt = dto.ProbedAt
                });
            }
        }

        var store = Store;
        var db = Db;
        if (store is null || db is null || Jobs is not { } jobStore)
            return StatusCode(503, new { error = "no database configured" });

        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });

        var platforms = (IDevicePlatforms)services.GetRequiredService(typeof(IDevicePlatforms));
        var time = (TimeProvider)services.GetRequiredService(typeof(TimeProvider));
        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));

        var row = await DeviceProbeRunner.ProbeOneAsync(
            device, $"admin:{currentUser.DiscordId}", platforms, jobStore, db, logger, time,
            HttpContext.RequestAborted);

        return Ok(new {
            id = device.Id,
            platform = device.Platform,
            label = device.Label,
            reachable = row.Reachable == true,
            installedAppVersion = row.AppVersion,
            installedBuild = row.Build,
            result = row.Outcome ?? "",
            note = row.Message,
            probedAt = row.StartedAt
        });
    }

    [HttpPost("refresh-all")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> RefreshAll() {
        if (RequireAdmin() is { } no) return no;

        if (services.GetService(typeof(IDeviceAgentClient)) is IDeviceAgentClient agent && agent.Enabled) {
            int probedByAgent = await agent.ProbeAllAsync(HttpContext.RequestAborted);
            return Ok(new { probed = probedByAgent });
        }

        var store = Store;
        var db = Db;
        if (store is null || db is null || Jobs is not { } jobStore)
            return StatusCode(503, new { error = "no database configured" });

        var platforms = (IDevicePlatforms)services.GetRequiredService(typeof(IDevicePlatforms));
        var time = (TimeProvider)services.GetRequiredService(typeof(TimeProvider));
        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));

        var devices = await store.EnabledDevicesAsync();
        int n = 0;
        foreach (var d in devices) {
            try {
                await DeviceProbeRunner.ProbeOneAsync(
                    d, $"admin-all:{currentUser.DiscordId}", platforms, jobStore, db, logger, time,
                    HttpContext.RequestAborted);
                n++;
            } catch (Exception ex) {
                logger.LogWarning(ex, "refresh-all: {Id} threw", d.Id);
            }
        }

        return Ok(new { probed = n });
    }

    [HttpPost("{id}/check-update")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> CheckUpdate(string id) {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        var db = Db;
        if (store is null || db is null) return StatusCode(503, new { error = "no database configured" });

        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));
        string who = currentUser.DiscordId ?? "?";

        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });

        var checker = services.GetServices<IDeviceStoreChecker>()
            .FirstOrDefault(c => string.Equals(c.Platform, device.Platform, StringComparison.OrdinalIgnoreCase));
        if (checker is null)
            return StatusCode(501, new { error = $"no store checker for platform {device.Platform}" });

        if (Jobs is not { } jobStore) return StatusCode(503, new { error = "no database configured" });
        var job = await jobStore.TryStartAsync(id, DeviceJobKinds.StoreCheck, $"admin:{who}", "checking store...",
            HttpContext.RequestAborted);
        if (job is null) return StatusCode(409, new { error = "another job is already running on this device" });

        logger.LogInformation("device check-update: {Id} start (by {Who})", id, who);
        var target = new DeviceTarget(device.Id, device.Platform, device.Target, device.Package);

        _ = Task.Run(() => RunCheckUpdateAsync(job, target, checker, who));

        return Accepted(new { id = device.Id, jobId = job.Id, action = "running" });
    }

    private async Task RunCheckUpdateAsync(JobRef job, DeviceTarget target, IDeviceStoreChecker checker,
        string who) {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILogger<DevicesController>>();
        var jobs = sp.GetRequiredService<DeviceJobStore>();
        try {
            var store = sp.GetService<IDeviceStatusStore>();
            var db = sp.GetService<EggIncognitoDbContext>();
            var platforms = sp.GetRequiredService<IDevicePlatforms>();
            if (store is null || db is null) {
                await jobs.FailAsync(job, "no database configured", CancellationToken.None);
                return;
            }

            var result = await checker.CheckAndUpdateAsync(target, CancellationToken.None,
                msg => jobs.ProgressAsync(job, msg).GetAwaiter().GetResult());

            var device = await store.GetAsync(job.DeviceId);
            if (device is not null) {
                var time = sp.GetRequiredService<TimeProvider>();
                await DeviceProbeRunner.ProbeOneAsync(
                    device, $"check-update:{who}", platforms, jobs, db, logger, time, CancellationToken.None);
            }

            await jobs.FinishAsync(job, result.Action, result.Note,
                new DeviceJobFacts(
                    AppVersion: result.InstalledAfter,
                    Detail: new { fromVersion = result.InstalledBefore, toVersion = result.InstalledAfter }),
                CancellationToken.None);
        } catch (Exception ex) {
            logger.LogError(ex, "device check-update: {Id} background run failed", job.DeviceId);
            await jobs.FailAsync(job, ex.Message, CancellationToken.None);
        }
    }

    [HttpPost("{id}/save")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Save(string id) {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(DeviceRegistryPublisher)) is not DeviceRegistryPublisher publisher)
            return StatusCode(503, new { error = "no database configured" });

        string who = currentUser.DiscordId ?? "?";
        var res = await publisher.PublishAsync(id, $"device-save:{who}", true, HttpContext.RequestAborted);
        return res.Outcome switch {
            PublishOutcome.Published =>
                Ok(new { saved = true, appVersion = res.AppVersion, build = res.Build }),
            PublishOutcome.UnknownDevice => NotFound(new { error = res.Error }),
            PublishOutcome.UnsupportedPlatform => StatusCode(501, new { error = res.Error }),
            PublishOutcome.NotConfigured => StatusCode(503, new { error = res.Error }),
            PublishOutcome.NotHarvested or PublishOutcome.StaleHarvest or PublishOutcome.MissingAsset =>
                StatusCode(409, new { error = res.Error }),
            _ => StatusCode(500, new { error = res.Error })
        };
    }

    [HttpGet("{id}/list-meshes")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> ListMeshes(string id) {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });
        if (services.GetService(typeof(DeviceAssetStore)) is not DeviceAssetStore assets)
            return StatusCode(503, new { error = "no database configured" });

        var heads = await assets.ListAsync(DeviceAssetKinds.Mesh, device.Platform, HttpContext.RequestAborted);
        return Ok(new { meshes = heads.Select(h => h.Name), harvested = heads.Count });
    }

    [HttpPost("{id}/poke")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Poke(string id) {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        if (await store.GetAsync(id) is null) return NotFound(new { error = "unknown device" });
        if (services.GetService(typeof(IDeviceAgentClient)) is not IDeviceAgentClient { Enabled: true } agent)
            return StatusCode(503, new { error = "no device agent configured (set DeviceAgent:Url + DeviceAgent:Secret)" });

        bool queued = await agent.PokeAsync(id, true, HttpContext.RequestAborted);
        return queued
            ? Accepted(new { ok = true, device = id, queued = true })
            : StatusCode(502, new { error = "device agent did not accept the poke" });
    }

    [HttpPost("poke-all")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> PokeAll() {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(IDeviceAgentClient)) is not IDeviceAgentClient { Enabled: true } agent)
            return StatusCode(503, new { error = "no device agent configured (set DeviceAgent:Url + DeviceAgent:Secret)" });

        bool queued = await agent.PokeAsync(null, true, HttpContext.RequestAborted);
        return queued
            ? Accepted(new { ok = true, queued = true })
            : StatusCode(502, new { error = "device agent did not accept the poke" });
    }

    [HttpGet("{id}/harvest")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Harvest(string id) {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(DeviceStateStore)) is not DeviceStateStore states)
            return StatusCode(503, new { error = "no database configured" });

        var ct = HttpContext.RequestAborted;
        var row = await states.GetAsync(id, ct);
        if (row is null) return NotFound(new { error = "no harvest state for device" });
        var cache = Timeline;
        var harvestJob = cache is null ? null : await cache.LatestAsync(id, DeviceJobKinds.Harvest, ct);
        IReadOnlyList<DeviceJobLineRow> entries = harvestJob is null || cache is null
            ? []
            : await cache.LinesAsync(id, harvestJob.Id, ct);
        bool inRegistry = !string.IsNullOrEmpty(row.Build)
                          && services.GetService(typeof(ProtoRegistryStore)) is ProtoRegistryStore registry
                          && await registry.GetAsync(row.Platform, row.Build, ct) is not null;
        return Ok(new {
            device = row.DeviceId,
            platform = row.Platform,
            appVersion = row.AppVersion,
            build = row.Build,
            inRegistry,
            revision = row.Revision,
            harvestedRevision = row.HarvestedRevision,
            stale = !string.Equals(row.Revision, row.HarvestedRevision, StringComparison.Ordinal),
            dirty = row.Dirty,
            harvesting = row.Harvesting,
            lastHarvestAt = row.LastHarvestAt,
            lastHarvestStatus = row.LastHarvestStatus,
            lastHarvestNote = row.LastHarvestNote,
            entries = entries.Select(e => {
                (string outcome, string? note) = SplitJobLine(e.Text);
                return new {
                    ranAt = e.At,
                    entry = e.Entry,
                    kind = (string?)null,
                    outcome,
                    note,
                    bytes = e.Bytes ?? 0,
                    sha256 = e.Sha256
                };
            })
        });
    }

    private static (string Outcome, string? Note) SplitJobLine(string? text) {
        if (string.IsNullOrWhiteSpace(text)) return ("ok", null);
        int sep = text.IndexOf(':', StringComparison.Ordinal);
        if (sep <= 0) return (text, null);
        string note = text[(sep + 1)..].Trim();
        return (text[..sep], note.Length == 0 ? null : note);
    }

    [HttpPost("{id}/restart-app")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> RestartApp(string id) {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(DeviceProxyPusher))
                is not DeviceProxyPusher pusher
            || services.GetService(typeof(IDeviceFleet))
                is not IDeviceFleet fleet)
            return StatusCode(503, new { error = "device capture not configured" });

        if (await FleetEntryAsync(fleet, id, HttpContext.RequestAborted) is not { } entry)
            return NotFound(new { error = "unknown device" });

        (bool ok, string? note) = await pusher.RestartAppAsync(entry, HttpContext.RequestAborted);
        return ok ? Ok(new { restarted = true, note }) : StatusCode(502, new { error = note ?? "restart failed" });
    }

    [HttpPost("{id}/recert")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Recert(string id, [FromQuery] int shots = 0, CancellationToken ct = default) {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(DeviceRecertService)) is not DeviceRecertService recert)
            return StatusCode(503, new { error = "recert is not configured (no database or android ui driver)" });

        string who = currentUser.DiscordId ?? "?";
        var result = await recert.RecertAsync(id, $"admin:{who}", ct);

        var dto = new RecertResultDto(
            result.Ok, result.Log, result.Fields, result.FailedStep, result.Shots.Count,
            shots == 1 ? [.. result.Shots.Select(s => new RecertShotDto(s.Label, Convert.ToBase64String(s.Png)))] : null);
        return Ok(dto);
    }

    [HttpGet("{id}/readiness")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Readiness(string id, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        (IActionResult? err, _, var target) = await ResolveUiAsync(id, ct);
        if (err is not null) return err;
        if (services.GetService(typeof(VirtualDeviceReadinessProbe)) is not VirtualDeviceReadinessProbe probe)
            return StatusCode(503, new { error = "readiness probe not configured" });

        return Ok(await probe.ProbeAsync(target, ct));
    }

    private ProvisionedInstanceStore? Instances =>
        services.GetService(typeof(ProvisionedInstanceStore)) as ProvisionedInstanceStore;

    private async Task<(IActionResult? Error, IDevicePlatform Platform, DeviceTarget Target)> ResolveUiAsync(
        string id, CancellationToken ct) {
        if (services.GetService(typeof(IDeviceFleet)) is not IDeviceFleet fleet)
            return (StatusCode(503, new { error = "device config not available" }), null!, null!);

        if (await FleetEntryAsync(fleet, id, ct) is not { } entry)
            return (NotFound(new { error = "unknown device" }), null!, null!);

        var target = new DeviceTarget(entry.Id, entry.Platform, entry.Target, entry.Package);
        if (services.GetService(typeof(IDevicePlatforms)) is not IDevicePlatforms platforms)
            return (StatusCode(503, new { error = "device platforms not available" }), null!, null!);

        return (null, platforms.For(target.Platform), target);
    }

    private ObjectResult UiFailure(DeviceOutcome outcome, string? note) => outcome switch {
        DeviceOutcome.Unsupported => StatusCode(501, new { error = note ?? "ui control is unsupported on this device" }),
        DeviceOutcome.Unreachable => StatusCode(502, new { error = note ?? "device unreachable" }),
        _ => StatusCode(500, new { error = note ?? "device ui call failed" })
    };

    [HttpGet("{id}/ui/screenshot")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("fetch")]
    public async Task<IActionResult> UiScreenshot(string id, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, ct);
        if (err is not null) return err;

        var shot = await platform.ScreenshotAsync(target, ct);
        if (!shot.Ok || shot.Value is not { Length: > 0 } png) return UiFailure(shot.Outcome, shot.Note);

        Response.Headers.CacheControl = "no-store";
        return File(png, "image/png");
    }

    [HttpGet("{id}/ui/screen")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> UiScreen(string id, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, ct);
        if (err is not null) return err;

        var size = await platform.ScreenSizeAsync(target, ct);
        if (!size.Ok) return UiFailure(size.Outcome, size.Note);

        Response.Headers.CacheControl = "no-store";
        return Ok(new UiScreenInfo(size.Value.Width, size.Value.Height));
    }

    [HttpGet("{id}/ui/stream")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [DisableRateLimiting]
    public async Task<IActionResult> UiStream(string id, [FromQuery] int fps = 3,
        [FromQuery] int quality = DeviceFrameEncoder.DefaultQuality, CancellationToken ct = default) {
        if (RequireAdmin() is { } no) return no;
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, ct);
        if (err is not null) return err;

        if (!DeviceStreamGate.TryEnter(target.Id))
            return StatusCode(409, new { error = "a screen stream is already open for this device" });

        int jpegQuality = DeviceFrameEncoder.ClampQuality(quality);
        var gap = TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(fps, MinStreamFps, MaxStreamFps));
        try {
            (byte[]? first, var outcome, string? note) = await FrameAsync(platform, target, jpegQuality, ct);
            if (first is null) return UiFailure(outcome, note);
            await PumpFramesAsync(platform, target, first, gap, jpegQuality, ct);
            return new EmptyResult();
        } catch (Exception ex) when (ex is OperationCanceledException or IOException
                                        or ObjectDisposedException) {
            return new EmptyResult();
        } finally {
            DeviceStreamGate.Exit(target.Id);
        }
    }

    private static async Task<(byte[]? Jpeg, DeviceOutcome Outcome, string? Note)> FrameAsync(
        IDevicePlatform platform, DeviceTarget target, int quality, CancellationToken ct) {
        var shot = await platform.ScreenshotAsync(target, ct);
        if (!shot.Ok || shot.Value is not { Length: > 0 } raw) return (null, shot.Outcome, shot.Note);

        byte[]? jpeg = await DeviceFrameEncoder.ToJpegAsync(raw, quality, ct);
        return jpeg is null
            ? (null, DeviceOutcome.Error, "the device frame could not be encoded")
            : (jpeg, DeviceOutcome.Ok, null);
    }

    private async Task PumpFramesAsync(IDevicePlatform platform, DeviceTarget target, byte[] first,
        TimeSpan gap, int quality, CancellationToken ct) {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = $"multipart/x-mixed-replace; boundary={StreamBoundary}";
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var body = Response.Body;
        byte[]? jpeg = first;
        while (jpeg is not null && !ct.IsCancellationRequested) {
            long started = Stopwatch.GetTimestamp();
            await body.WriteAsync(PartHeader(jpeg.Length), ct);
            await body.WriteAsync(jpeg, ct);
            await body.WriteAsync(PartTrailer, ct);
            await body.FlushAsync(ct);

            var spent = Stopwatch.GetElapsedTime(started);
            if (spent < gap) await Task.Delay(gap - spent, ct);
            if (ct.IsCancellationRequested) return;
            (jpeg, _, _) = await FrameAsync(platform, target, quality, ct);
        }

        if (ct.IsCancellationRequested) return;
        await body.WriteAsync(StreamEnd, ct);
        await body.FlushAsync(ct);
    }

    private static byte[] PartHeader(int length) => Encoding.ASCII.GetBytes(
        $"--{StreamBoundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");

    [HttpGet("{id}/ui/video")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [DisableRateLimiting]
    public async Task<IActionResult> UiVideo(string id, [FromQuery] string size = "720x1280",
        [FromQuery] int bitrate = 3_000_000, CancellationToken ct = default) {
        if (RequireAdmin() is { } no) return no;
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, ct);
        if (err is not null) return err;
        if (!Platforms.Matches(platform.Platform, Platforms.Android))
            return BadRequest(new { error = "video streaming is android only" });
        if (!VideoSizeRegex().IsMatch(size))
            return BadRequest(new { error = "size must look like WIDTHxHEIGHT, e.g. 720x1280" });

        if (services.GetService(typeof(IDeviceConnectionFactory)) is not IDeviceConnectionFactory factory)
            return StatusCode(503, new { error = "device transport not configured" });
        var conn = factory.For(target);
        if (conn is null) return StatusCode(502, new { error = "no connection for device" });
        if (!conn.SupportsExecOut) return StatusCode(501, new { error = "this connection cannot stream exec-out" });

        var display = await platform.ScreenSizeAsync(target, ct);
        if (display.Ok) {
            string[] box = size.Split('x');
            size = ScreenVideoPump.FitSize(display.Value.Width, display.Value.Height,
                int.Parse(box[0], CultureInfo.InvariantCulture), int.Parse(box[1], CultureInfo.InvariantCulture));
        }

        if (!DeviceStreamGate.TryEnter(target.Id))
            return StatusCode(409, new { error = "a screen stream is already open for this device" });

        string command = ScreenVideoPump.ScreenrecordCommand(size, Math.Clamp(bitrate, MinVideoBitrate, MaxVideoBitrate));
        try {
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "application/octet-stream";
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Pragma = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";
            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            var pump = new ScreenVideoPump(token => conn.ExecOutStreamAsync(command, token));
            string? note = await pump.RunAsync(Response.Body, ct);
            if (note is null) return new EmptyResult();

            (services.GetService(typeof(ILogger<DevicesController>)) as ILogger<DevicesController>)?
                .LogWarning("video stream for {DeviceId} stopped: {Note}", target.Id, note);
            if (Response.HasStarted) return new EmptyResult();
            Response.Clear();
            return StatusCode(502, new { error = note });
        } catch (Exception ex) when (ex is OperationCanceledException or IOException
                                        or ObjectDisposedException) {
            return new EmptyResult();
        } finally {
            DeviceStreamGate.Exit(target.Id);
        }
    }

    [HttpGet("{id}/ui/dump")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> UiDump(string id, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, ct);
        if (err is not null) return err;

        var dump = await platform.DumpUiAsync(target, ct);
        if (!dump.Ok || dump.Value is not { } tree) return UiFailure(dump.Outcome, dump.Note);

        Response.Headers.CacheControl = "no-store";
        return Ok(UiTreeProjector.Project(tree));
    }

    [HttpPost("{id}/ui/tap")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> UiTap(string id, [FromBody] UiTapRequest req, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (req.X < 0 || req.Y < 0) return BadRequest(new { error = "x and y must be non-negative" });
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, ct);
        if (err is not null) return err;

        var r = await platform.TapPointAsync(target, req.X, req.Y, ct);
        return r.Ok ? Ok(new UiActionResult(true, DeviceOutcomes.Label(r), r.Note)) : UiFailure(r.Outcome, r.Note);
    }

    [HttpPost("{id}/ui/touch")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> UiTouch(string id, [FromBody] UiTouchRequest req, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (req.X < 0 || req.Y < 0) return BadRequest(new { error = "x and y must be non-negative" });
        if (!Enum.TryParse(req.Phase, true, out TouchPhase phase))
            return BadRequest(new { error = "phase must be down, move, up or cancel" });
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, ct);
        if (err is not null) return err;

        var r = await platform.TouchAsync(target, phase, req.X, req.Y, ct);
        return r.Ok ? Ok(new UiActionResult(true, DeviceOutcomes.Label(r), r.Note)) : UiFailure(r.Outcome, r.Note);
    }

    [HttpPost("{id}/ui/swipe")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> UiSwipe(string id, [FromBody] UiSwipeRequest req, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (req.X1 < 0 || req.Y1 < 0 || req.X2 < 0 || req.Y2 < 0)
            return BadRequest(new { error = "coordinates must be non-negative" });
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, ct);
        if (err is not null) return err;

        var r = await platform.SwipeAsync(target, req.X1, req.Y1, req.X2, req.Y2, req.DurationMs, ct);
        return r.Ok ? Ok(new UiActionResult(true, DeviceOutcomes.Label(r), r.Note)) : UiFailure(r.Outcome, r.Note);
    }

    [HttpPost("{id}/ui/text")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> UiText(string id, [FromBody] UiTextRequest req, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (string.IsNullOrEmpty(req.Text)) return BadRequest(new { error = "text required" });
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, ct);
        if (err is not null) return err;

        var r = await platform.InputTextAsync(target, req.Text, ct);
        return r.Ok ? Ok(new UiActionResult(true, DeviceOutcomes.Label(r), r.Note)) : UiFailure(r.Outcome, r.Note);
    }

    [HttpPost("{id}/ui/key")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> UiKey(string id, [FromBody] UiKeyRequest req, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (!DeviceKeyNames.TryParse(req.Key, out var key))
            return BadRequest(new { error = $"unknown key (expected one of: {string.Join(", ", DeviceKeyNames.All)})" });
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, ct);
        if (err is not null) return err;

        var r = await platform.KeyAsync(target, key, ct);
        return r.Ok ? Ok(new UiActionResult(true, DeviceOutcomes.Label(r), r.Note)) : UiFailure(r.Outcome, r.Note);
    }

    [HttpGet("{id}/live")]
    [EnableRateLimiting("fetch")]
    public async Task<IActionResult> Live(string id, CancellationToken ct) {
        if (await ResolveDeviceIdAsync(id, ct) is not { } realId) return Ok(new { found = false });
        if (services.GetService(typeof(DeviceCaptureManager)) is not DeviceCaptureManager mgr)
            return Ok(new { found = false });
        bool isAdmin = currentUser.IsAtLeast(UserRole.Admin);
        var d = mgr.DiagFor(realId);
        object capture = new {
            listening = mgr.PortFor(realId) != 0,
            port = isAdmin ? mgr.PortFor(realId) : 0,
            clientConnects = d.ClientConnects,
            auxbrainConnects = d.AuxbrainConnects,
            flows = d.Flows,
            rinfoHarvests = d.RinfoHarvests,
            lastDecryptError = isAdmin ? d.LastDecryptError : null,
            recentConnects = isAdmin ? d.RecentConnects : null
        };
        var v = mgr.Rinfo.Latest(realId);
        if (v is null) return Ok(new { found = false, capture });
        return isAdmin
            ? Ok(new { found = true, v.DeviceId, v.Platform, v.Version, v.Build, v.ClientVersion, v.LastSeen, capture })
            : Ok(new { found = true, v.Platform, v.Version, v.Build, v.ClientVersion, capture });
    }
}
