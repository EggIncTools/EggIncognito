using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EggIdentity.Auth;
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
    IServiceScopeFactory scopeFactory,
    ILogger<DevicesController> logger) : ApiControllerBase {
    private const string StreamBoundary = "egiframe";
    private const int MinStreamFps = 1;
    private const int MaxStreamFps = 5;
    private const int MinVideoBitrate = 500_000;
    private const int MaxVideoBitrate = 8_000_000;

    [GeneratedRegex(@"^\d{2,4}x\d{2,4}$")]
    private static partial Regex VideoSizeRegex();

    private static readonly byte[] PartTrailer = "\r\n"u8.ToArray();
    private static readonly byte[] StreamEnd = Encoding.ASCII.GetBytes($"--{StreamBoundary}--\r\n");

    private static async Task<DeviceEntry?> FleetEntryAsync(IDeviceFleet fleet, string id, CancellationToken ct) =>
        (await fleet.EnabledAsync(ct)).FirstOrDefault(d => d.Id == id);

    private async Task<string?> ResolveDeviceIdAsync(string incoming, IDeviceStatusStore? store,
        CancellationToken ct) {
        if (currentUser.Current.IsAtLeast(UserRole.Admin)) return incoming;
        if (store is null) return null;
        var enabled = await store.EnabledDevicesAsync(ct);
        return enabled.FirstOrDefault(d => DevicePublicKey.For(d.Id) == incoming)?.Id;
    }

    [HttpGet("status")]
    [EnableRateLimiting("fetch")]
    public async Task<IActionResult> Status([FromServices] IDeviceFleet? fleet,
        [FromServices] DeviceTimelineCache? timeline, [FromServices] EggIncognitoDbContext? db,
        [FromServices] DeviceCaptureManager? captures, [FromServices] GameBinaryProvider? binaryProvider,
        [FromServices] DeviceStateStore? deviceStates) {
        if (fleet is null || timeline is null) return Ok(Array.Empty<DeviceStatusRow>());

        var ct = HttpContext.RequestAborted;
        bool isAdmin = currentUser.Current.IsAtLeast(UserRole.Admin);
        var devices = (await fleet.EnabledAsync(ct)).Select(AsDevice).ToDictionary(d => d.Id);

        var ids = devices.Keys.ToList();
        var probes = (await timeline.LatestPerDeviceAsync(ids, DeviceJobKinds.Probe, ct))
            .ToDictionary(p => p.DeviceId, StringComparer.Ordinal);
        var updates = (await timeline.LatestPerDeviceAsync(ids, DeviceJobKinds.StoreCheck, ct))
            .ToDictionary(u => u.DeviceId, StringComparer.Ordinal);

        var platforms = devices.Values.Select(d => d.Platform).Distinct().ToList();
        var versions = db is null
            ? DeviceVersionIndex.Empty
            : await DeviceVersionIndex.BuildAsync(db, platforms, ct);
        var storeLatest = await StoreLatestPerPlatformAsync(db, platforms, ct);

        int capturePortFor(string id) =>
            captures?.PortFor(id) is > 0 and var listening ? listening : devices[id].CapturePort ?? 0;
        var inputs = new DeviceStatusInputs(
            isAdmin, probes, updates, storeLatest, versions,
            await CapturedClientVersionsAsync(deviceStates, ct),
            binaryProvider, capturePortFor);

        return Ok(devices.Values.Select(d => DeviceStatusProjector.Project(d, inputs)));
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

    private static async Task<Dictionary<string, int>> CapturedClientVersionsAsync(
        DeviceStateStore? deviceStates, CancellationToken ct) {
        var captured = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (deviceStates is null) return captured;

        foreach (var s in await deviceStates.ListAsync(ct)) {
            if (s.ClientVersion is { } clientVersion) captured[s.DeviceId] = clientVersion;
        }

        return captured;
    }

    [HttpGet("{id}/jobs")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> JobHistory(string id, [FromServices] DeviceJobFeed feed,
        [FromQuery] int take = JobGroupCollapser.DefaultTake, [FromQuery] long? before = null,
        CancellationToken ct = default) =>
        Ok(await feed.PageAsync(id, take, before, ct));

    [HttpGet("{id}/jobs/{jobId:long}/lines")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> JobLines(string id, long jobId, [FromServices] DeviceJobFeed feed,
        CancellationToken ct = default) =>
        Ok(await feed.LinesAsync(id, jobId, ct));

    [HttpGet("jobs/live")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("fetch")]
    public async Task<IActionResult> LiveJobs([FromServices] DeviceJobFeed? feed, CancellationToken ct) =>
        Ok(feed is null ? [] : await feed.LiveAsync(ct));

    [HttpPost("{id}/refresh")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Refresh(string id, [FromServices] IDeviceAgentClient? agent,
        [FromServices] IDeviceStatusStore? store, [FromServices] EggIncognitoDbContext? db,
        [FromServices] DeviceJobStore? jobStore, [FromServices] IDevicePlatforms platforms,
        [FromServices] TimeProvider time) {
        if (agent is { Enabled: true }) {
            var dto = await agent.ProbeAsync(id, HttpContext.RequestAborted);
            if (dto is not null) {
                var agentDevice = store is null ? null : await store.GetAsync(id, HttpContext.RequestAborted);
                if (agentDevice is null) return Fail(404, "unknown device");
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

        if (store is null || db is null || jobStore is null) return Fail(503, "no database configured");

        var device = await store.GetAsync(id);
        if (device is null) return Fail(404, "unknown device");

        var row = await DeviceProbeRunner.ProbeOneAsync(
            device, $"admin:{currentUser.Current.DiscordId}", platforms, jobStore, db, logger, time,
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
    public async Task<IActionResult> RefreshAll([FromServices] IDeviceAgentClient? agent,
        [FromServices] IDeviceStatusStore? store, [FromServices] EggIncognitoDbContext? db,
        [FromServices] DeviceJobStore? jobStore, [FromServices] IDevicePlatforms platforms,
        [FromServices] TimeProvider time) {
        if (agent is { Enabled: true }) {
            int probedByAgent = await agent.ProbeAllAsync(HttpContext.RequestAborted);
            return Ok(new { probed = probedByAgent });
        }

        if (store is null || db is null || jobStore is null) return Fail(503, "no database configured");

        var devices = await store.EnabledDevicesAsync();
        int n = 0;
        foreach (var d in devices) {
            try {
                await DeviceProbeRunner.ProbeOneAsync(
                    d, $"admin-all:{currentUser.Current.DiscordId}", platforms, jobStore, db, logger, time,
                    HttpContext.RequestAborted);
                n++;
            } catch (Exception ex) {
                logger.LogWarning(ex, "refresh-all: {Id} threw", d.Id);
            }
        }

        return Ok(new { probed = n });
    }

    [HttpPost("ipatool/check-versions")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<IpaStoreVersionChecker>("ipatool checker not configured")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> IpaToolCheckVersions([FromServices] IpaStoreVersionChecker checker) {
        var result = await checker.CheckAsync(HttpContext.RequestAborted);
        if (!result.Ok) return Fail(502, result.Diagnostics);
        return Ok(new { versions = result.Versions, diagnostics = result.Diagnostics });
    }

    [HttpPost("{id}/check-update")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> CheckUpdate(string id, [FromServices] IDeviceStatusStore store,
        [FromServices] DeviceJobStore jobStore, [FromServices] IEnumerable<IDeviceStoreChecker> checkers) {
        string who = currentUser.Current.DiscordId ?? "?";

        var device = await store.GetAsync(id);
        if (device is null) return Fail(404, "unknown device");

        var checker = checkers.FirstOrDefault(c =>
            string.Equals(c.Platform, device.Platform, StringComparison.OrdinalIgnoreCase));
        if (checker is null) return Fail(501, $"no store checker for platform {device.Platform}");

        var job = await jobStore.TryStartAsync(id, DeviceJobKinds.StoreCheck, $"admin:{who}", "checking store...",
            HttpContext.RequestAborted);
        if (job is null) return Fail(409, "another job is already running on this device");

        logger.LogInformation("device check-update: {Id} start (by {Who})", id, who);
        var target = new DeviceTarget(device.Id, device.Platform, device.Target, device.Package);

        _ = Task.Run(() => RunCheckUpdateAsync(job, target, checker, who));

        return Accepted(new { id = device.Id, jobId = job.Id, action = "running" });
    }

    private async Task RunCheckUpdateAsync(JobRef job, DeviceTarget target, IDeviceStoreChecker checker,
        string who) {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var scopedLogger = sp.GetRequiredService<ILogger<DevicesController>>();
        var jobs = sp.GetService<DeviceJobStore>();
        try {
            var store = sp.GetService<IDeviceStatusStore>();
            var db = sp.GetService<EggIncognitoDbContext>();
            var platforms = sp.GetRequiredService<IDevicePlatforms>();
            if (store is null || db is null) {
                if (jobs is not null) await jobs.FailAsync(job, "no database configured", CancellationToken.None);
                return;
            }

            var result = await checker.CheckAndUpdateAsync(target, CancellationToken.None,
                jobs is null ? null : msg => jobs.ProgressAsync(job, msg));

            var device = await store.GetAsync(job.DeviceId);
            if (device is not null && jobs is not null) {
                var time = sp.GetRequiredService<TimeProvider>();
                await DeviceProbeRunner.ProbeOneAsync(
                    device, $"check-update:{who}", platforms, jobs, db, scopedLogger, time, CancellationToken.None);
            }

            if (jobs is not null)
                await jobs.FinishAsync(job, result.Action, result.Note,
                    new DeviceJobFacts(
                        AppVersion: result.InstalledAfter,
                        Detail: new { fromVersion = result.InstalledBefore, toVersion = result.InstalledAfter }),
                    CancellationToken.None);
        } catch (Exception ex) {
            scopedLogger.LogError(ex, "device check-update: {Id} background run failed", job.DeviceId);
            if (jobs is not null) await jobs.FailAsync(job, ex.Message, CancellationToken.None);
        }
    }

    [HttpPost("{id}/save")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Save(string id, [FromServices] DeviceRegistryPublisher publisher) {
        string who = currentUser.Current.DiscordId ?? "?";
        var res = await publisher.PublishAsync(id, $"device-save:{who}", true, HttpContext.RequestAborted);
        return res.Outcome switch {
            PublishOutcome.Published =>
                Ok(new { saved = true, appVersion = res.AppVersion, build = res.Build }),
            PublishOutcome.UnknownDevice => Fail(404, res.Error ?? ""),
            PublishOutcome.UnsupportedPlatform => Fail(501, res.Error ?? ""),
            PublishOutcome.NotConfigured => Fail(503, res.Error ?? ""),
            PublishOutcome.NotHarvested or PublishOutcome.StaleHarvest or PublishOutcome.MissingAsset =>
                Fail(409, res.Error ?? ""),
            _ => Fail(500, res.Error ?? "")
        };
    }

    [HttpGet("{id}/list-meshes")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> ListMeshes(string id, [FromServices] IDeviceStatusStore store,
        [FromServices] DeviceAssetStore assets) {
        var device = await store.GetAsync(id);
        if (device is null) return Fail(404, "unknown device");

        var heads = await assets.ListAsync(DeviceAssetKinds.Mesh, device.Platform, HttpContext.RequestAborted);
        return Ok(new { meshes = heads.Select(h => h.Name), harvested = heads.Count });
    }

    [HttpPost("{id}/poke")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Poke(string id, [FromServices] IDeviceStatusStore store,
        [FromServices] IDeviceAgentClient? agent) {
        if (await store.GetAsync(id) is null) return Fail(404, "unknown device");
        if (agent is not { Enabled: true }) return Fail(503, "no device agent on this host");

        bool queued = await agent.PokeAsync(id, true, HttpContext.RequestAborted);
        return queued
            ? Accepted(new { ok = true, device = id, queued = true })
            : Fail(502, "device agent did not accept the poke");
    }

    [HttpPost("poke-all")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> PokeAll([FromServices] IDeviceAgentClient? agent) {
        if (agent is not { Enabled: true }) return Fail(503, "no device agent on this host");

        bool queued = await agent.PokeAsync(null, true, HttpContext.RequestAborted);
        return queued
            ? Accepted(new { ok = true, queued = true })
            : Fail(502, "device agent did not accept the poke");
    }

    [HttpGet("{id}/harvest")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Harvest(string id, [FromServices] DeviceStateStore states,
        [FromServices] DeviceTimelineCache? cache, [FromServices] ProtoRegistryStore? registry) {
        var ct = HttpContext.RequestAborted;
        var row = await states.GetAsync(id, ct);
        if (row is null) return Fail(404, "no harvest state for device");
        var harvestJob = cache is null ? null : await cache.LatestAsync(id, DeviceJobKinds.Harvest, ct);
        IReadOnlyList<DeviceJobLineRow> entries = harvestJob is null || cache is null
            ? []
            : await cache.LinesAsync(id, harvestJob.Id, ct);
        bool inRegistry = row.Build is { Length: > 0 }
                          && registry is not null
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
    [Requires<DeviceProxyPusher>("device capture not configured")]
    [Requires<IDeviceFleet>("device capture not configured")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> RestartApp(string id, [FromServices] DeviceProxyPusher pusher,
        [FromServices] IDeviceFleet fleet) {
        if (await FleetEntryAsync(fleet, id, HttpContext.RequestAborted) is not { } entry)
            return Fail(404, "unknown device");

        (bool ok, string? note) = await pusher.RestartAppAsync(entry, HttpContext.RequestAborted);
        return ok ? Ok(new { restarted = true, note }) : Fail(502, note ?? "restart failed");
    }

    [HttpPost("{id}/recert")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<DeviceRecertService>("recert is not configured (no database or android ui driver)")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Recert(string id, [FromServices] DeviceRecertService recert,
        [FromQuery] int shots = 0, CancellationToken ct = default) {
        string who = currentUser.Current.DiscordId ?? "?";
        var result = await recert.RecertAsync(id, $"admin:{who}", ct);

        var dto = new RecertResultDto(
            result.Ok, result.Log, result.Fields, result.FailedStep, result.Shots.Count,
            shots == 1 ? [.. result.Shots.Select(s => new RecertShotDto(s.Label, Convert.ToBase64String(s.Png)))] : null);
        return Ok(dto);
    }

    [HttpGet("{id}/readiness")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<AndroidReadinessProbe>("readiness probe not configured")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Readiness(string id, [FromServices] AndroidReadinessProbe probe,
        [FromServices] IDeviceFleet? fleet, [FromServices] IDevicePlatforms? platforms, CancellationToken ct) {
        (IActionResult? err, _, var target) = await ResolveUiAsync(id, fleet, platforms, ct);
        if (err is not null) return err;

        return Ok(await probe.ProbeAsync(target, ct));
    }

    private sealed record UiResolution(IActionResult? Error, IDevicePlatform Platform, DeviceTarget Target);

    private async Task<UiResolution> ResolveUiAsync(
        string id, IDeviceFleet? fleet, IDevicePlatforms? platforms, CancellationToken ct) {
        if (fleet is null)
            return new UiResolution(Fail(503, "device config not available"), new NullDevicePlatform(),
                new DeviceTarget("", "", "", ""));

        if (await FleetEntryAsync(fleet, id, ct) is not { } entry)
            return new UiResolution(Fail(404, "unknown device"), new NullDevicePlatform(), new DeviceTarget("", "", "", ""));

        var target = new DeviceTarget(entry.Id, entry.Platform, entry.Target, entry.Package);
        if (platforms is null)
            return new UiResolution(Fail(503, "device platforms not available"), new NullDevicePlatform(), target);

        return new UiResolution(null, platforms.For(target.Platform), target);
    }

    private ObjectResult UiFailure(DeviceOutcome outcome, string? note) => outcome switch {
        DeviceOutcome.Unsupported => Fail(501, note ?? "ui control is unsupported on this device"),
        DeviceOutcome.Unreachable => Fail(502, note ?? "device unreachable"),
        _ => Fail(500, note ?? "device ui call failed")
    };

    [HttpGet("{id}/ui/screenshot")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("fetch")]
    public async Task<IActionResult> UiScreenshot(string id, [FromServices] IDeviceFleet? fleet,
        [FromServices] IDevicePlatforms? platforms, CancellationToken ct) {
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, fleet, platforms, ct);
        if (err is not null) return err;

        var shot = await platform.ScreenshotAsync(target, ct);
        if (!shot.Ok || shot.Value is not { Length: > 0 } png) return UiFailure(shot.Outcome, shot.Note);

        Response.Headers.CacheControl = "no-store";
        return File(png, "image/png");
    }

    [HttpGet("{id}/ui/screen")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> UiScreen(string id, [FromServices] IDeviceFleet? fleet,
        [FromServices] IDevicePlatforms? platforms, CancellationToken ct) {
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, fleet, platforms, ct);
        if (err is not null) return err;

        var size = await platform.ScreenSizeAsync(target, ct);
        if (!size.Ok) return UiFailure(size.Outcome, size.Note);

        Response.Headers.CacheControl = "no-store";
        return Ok(new UiScreenInfo(size.Value.Width, size.Value.Height));
    }

    [HttpGet("{id}/ui/state")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> UiState(string id, [FromServices] IDeviceFleet? fleet,
        [FromServices] IDevicePlatforms? platforms, CancellationToken ct) {
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, fleet, platforms, ct);
        if (err is not null) return err;

        var state = await platform.ScreenStateAsync(target, ct);
        if (!state.Ok) return UiFailure(state.Outcome, state.Note);

        Response.Headers.CacheControl = "no-store";
        return Ok(new UiStateInfo(state.Value.Awake, state.Value.Locked, state.Value.NavMode));
    }

    [HttpGet("{id}/ui/stream")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [DisableRateLimiting]
    public async Task<IActionResult> UiStream(string id, [FromServices] IDeviceFleet? fleet,
        [FromServices] IDevicePlatforms? platforms, [FromQuery] int fps = 3,
        [FromQuery] int quality = DeviceFrameEncoder.DefaultQuality, CancellationToken ct = default) {
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, fleet, platforms, ct);
        if (err is not null) return err;

        (var lease, var busy) = await EnterStreamGateAsync(target.Id, ct);
        if (lease is null) return busy!;

        int jpegQuality = DeviceFrameEncoder.ClampQuality(quality);
        var gap = TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(fps, MinStreamFps, MaxStreamFps));
        using (lease) {
            var live = lease.Token;
            try {
                await using var hold = await DeviceStreamHold.AcquireAsync(services, target, live);
                (byte[]? first, var outcome, string? note) = await FrameAsync(platform, target, jpegQuality, live);
                if (first is null) return UiFailure(outcome, note);
                await PumpFramesAsync(platform, target, first, gap, jpegQuality, live);
                return new EmptyResult();
            } catch (Exception ex) when (ex is OperationCanceledException or IOException
                                            or ObjectDisposedException) {
                logger.LogDebug(ex, "ui frame stream closed{Why}", lease.Preempted ? " (a newer viewer took over)" : "");
                return new EmptyResult();
            }
        }
    }

    private async Task<(DeviceStreamLease? Lease, IActionResult? Busy)> EnterStreamGateAsync(string deviceId,
        CancellationToken ct) {
        try {
            if (await DeviceStreamGate.TryEnterAsync(deviceId, ct) is { } lease) return (lease, null);
        } catch (OperationCanceledException) {
            return (null, new EmptyResult());
        }

        return (null, Fail(409,
            $"a screen stream is still open for this device after waiting {DeviceStreamGate.HandoverWait.TotalSeconds:0}s"));
    }

    private static async Task<(byte[]? Jpeg, DeviceOutcome Outcome, string? Note)> FrameAsync(
        IDevicePlatform platform, DeviceTarget target, int quality, CancellationToken ct) {
        var shot = await platform.ScreenshotAsync(target, ct);
        if (!shot.Ok || shot.Value is not { Length: > 0 } raw) return (null, shot.Outcome, shot.Note);

        byte[]? jpeg = DeviceFrameEncoder.ToJpeg(raw, quality);
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
    public async Task<IActionResult> UiVideo(string id, [FromServices] IDeviceFleet? fleet,
        [FromServices] IDevicePlatforms? platforms, [FromQuery] string size = "720x1280",
        [FromQuery] int bitrate = 3_000_000, CancellationToken ct = default) {
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, fleet, platforms, ct);
        if (err is not null) return err;
        if (!platform.Capabilities.HasFlag(DeviceCapabilities.ScreenStream))
            return Fail(501, $"{platform.Platform} cannot stream video");
        if (!VideoSizeRegex().IsMatch(size))
            return Fail(400, "size must look like WIDTHxHEIGHT, e.g. 720x1280");

        var display = await platform.ScreenSizeAsync(target, ct);
        if (display.Ok) {
            string[] box = size.Split('x');
            size = ScreenVideoPump.FitSize(display.Value.Width, display.Value.Height,
                int.Parse(box[0], CultureInfo.InvariantCulture), int.Parse(box[1], CultureInfo.InvariantCulture));
        }

        (var lease, var busy) = await EnterStreamGateAsync(target.Id, ct);
        if (lease is null) return busy!;

        string[] wh = size.Split('x');
        var options = new ScreenStreamOptions(
            int.Parse(wh[0], CultureInfo.InvariantCulture),
            int.Parse(wh[1], CultureInfo.InvariantCulture),
            Math.Clamp(bitrate, MinVideoBitrate, MaxVideoBitrate));
        using (lease) {
            var live = lease.Token;
            try {
                await using var hold = await DeviceStreamHold.AcquireAsync(services, target, live);
                Response.StatusCode = StatusCodes.Status200OK;
                Response.ContentType = "application/octet-stream";
                Response.Headers.CacheControl = "no-store";
                Response.Headers.Pragma = "no-cache";
                Response.Headers["X-Accel-Buffering"] = "no";
                HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

                string? note = await platform.StreamScreenAsync(target, options, Response.Body, live);
                if (note is null) return new EmptyResult();

                logger.LogWarning("video stream for {DeviceId} stopped: {Note}", target.Id, note);
                if (Response.HasStarted) return new EmptyResult();
                Response.Clear();
                return Fail(502, note);
            } catch (Exception ex) when (ex is OperationCanceledException or IOException
                                            or ObjectDisposedException) {
                logger.LogDebug(ex, "ui video stream closed{Why}", lease.Preempted ? " (a newer viewer took over)" : "");
                return new EmptyResult();
            }
        }
    }

    [HttpPost("{id}/ui/tap")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> UiTap(string id, [FromBody] UiTapRequest req,
        [FromServices] IDeviceFleet? fleet, [FromServices] IDevicePlatforms? platforms, CancellationToken ct) {
        if (req.X < 0 || req.Y < 0) return Fail(400, "x and y must be non-negative");
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, fleet, platforms, ct);
        if (err is not null) return err;

        var r = await DeviceInputLane.RunAsync(target.Id, t => platform.TapPointAsync(target, req.X, req.Y, t), ct);
        return r.Ok ? Ok(new UiActionResult(true, DeviceOutcomes.Label(r), r.Note)) : UiFailure(r.Outcome, r.Note);
    }

    [HttpPost("{id}/ui/watch")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<PixelWatchService>("pixel watch not configured")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> UiWatch(string id, [FromBody] PixelWatchRequest req,
        [FromServices] PixelWatchService watches, [FromServices] IDeviceFleet? fleet,
        [FromServices] IDevicePlatforms? platforms, CancellationToken ct) {
        if (req.X < 0 || req.Y < 0) return Fail(400, "x and y must be non-negative");
        if (req.Kind is { Length: > 0 } && !PixelWatchKinds.IsKnown(req.Kind))
            return Fail(400, $"kind must be one of: {string.Join(", ", PixelWatchKinds.All)}");
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, fleet, platforms, ct);
        if (err is not null) return err;

        var r = await watches.AddAsync(platform, target, req, ct);
        return r.Ok ? Ok(r.Value) : UiFailure(r.Outcome, r.Note);
    }

    [HttpPatch("{id}/ui/watch/{point}")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<PixelWatchService>("pixel watch not configured")]
    [EnableRateLimiting("write")]
    public IActionResult UiWatchUpdate(string id, string point, [FromBody] PixelWatchPointUpdate req,
        [FromServices] PixelWatchService watches) {
        if (req.Kind is { Length: > 0 } && !PixelWatchKinds.IsKnown(req.Kind))
            return Fail(400, $"kind must be one of: {string.Join(", ", PixelWatchKinds.All)}");
        if (!watches.Update(id, point, req)) return Fail(404, "unknown watch point");
        return Ok(watches.State(id));
    }

    [HttpPost("{id}/ui/watch/groups")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<PixelWatchService>("pixel watch not configured")]
    [EnableRateLimiting("write")]
    public IActionResult UiWatchGroup(string id, [FromBody] PixelWatchGroupRequest req,
        [FromServices] PixelWatchService watches) {
        var r = watches.AddGroup(id, req);
        return r.Ok ? Ok(r.Value) : Fail(400, r.Note ?? "group failed");
    }

    [HttpDelete("{id}/ui/watch/groups/{group}")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<PixelWatchService>("pixel watch not configured")]
    [EnableRateLimiting("write")]
    public IActionResult UiWatchUngroup(string id, string group, [FromServices] PixelWatchService watches) {
        if (!watches.Ungroup(id, group)) return Fail(404, "unknown watch group");
        return Ok(watches.State(id));
    }

    [HttpGet("{id}/ui/watch")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<PixelWatchService>("pixel watch not configured")]
    [EnableRateLimiting("read")]
    public IActionResult UiWatchStatus(string id, [FromServices] PixelWatchService watches) =>
        Ok(watches.State(id));

    [HttpDelete("{id}/ui/watch")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<PixelWatchService>("pixel watch not configured")]
    [EnableRateLimiting("write")]
    public IActionResult UiWatchStop(string id, [FromServices] PixelWatchService watches,
        [FromQuery] string? point) {
        bool stopped = point is not { Length: > 0 } ? watches.StopAll(id) : watches.Remove(id, point);
        return Ok(new { ok = true, stopped, state = watches.State(id) });
    }

    [HttpPost("{id}/ui/watch/pause")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<PixelWatchService>("pixel watch not configured")]
    [EnableRateLimiting("write")]
    public IActionResult UiWatchPause(string id, [FromServices] PixelWatchService watches, [FromQuery] bool on) {
        if (!watches.SetPaused(id, on)) return Fail(404, "no watch points are armed for this device");
        return Ok(watches.State(id));
    }

    [HttpPost("{id}/ui/watch/client")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<PixelWatchService>("pixel watch not configured")]
    [EnableRateLimiting("write")]
    public IActionResult UiWatchClient(string id, [FromServices] PixelWatchService watches, [FromQuery] bool on) {
        watches.SetClientWatching(id, on);
        return Ok(new { ok = true, client = on, state = watches.State(id) });
    }

    [HttpPost("{id}/ui/watch/{point}/hit")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<PixelWatchService>("pixel watch not configured")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> UiWatchHit(string id, string point, [FromServices] PixelWatchService watches,
        [FromServices] IDeviceFleet? fleet, [FromServices] IDevicePlatforms? platforms, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(point)) return Fail(400, "point is required");
        if (watches.State(id).Points.All(p => p.Id != point)) return Fail(404, "unknown watch point");
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, fleet, platforms, ct);
        if (err is not null) return err;

        var r = await watches.HitAsync(platform, target, point, ct);
        return r.Ok ? Ok(new { ok = true, note = r.Note, state = r.Value }) : UiFailure(r.Outcome, r.Note);
    }

    [HttpPost("{id}/ui/touch")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> UiTouch(string id, [FromBody] UiTouchRequest req,
        [FromServices] IDeviceFleet? fleet, [FromServices] IDevicePlatforms? platforms, CancellationToken ct) {
        if (req.X < 0 || req.Y < 0) return Fail(400, "x and y must be non-negative");
        if (!Enum.TryParse(req.Phase, true, out TouchPhase phase))
            return Fail(400, "phase must be down, move, up or cancel");
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, fleet, platforms, ct);
        if (err is not null) return err;

        var r = await DeviceInputLane.RunAsync(target.Id, t => platform.TouchAsync(target, phase, req.X, req.Y, t), ct);
        return r.Ok ? Ok(new UiActionResult(true, DeviceOutcomes.Label(r), r.Note)) : UiFailure(r.Outcome, r.Note);
    }

    [HttpPost("{id}/ui/swipe")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> UiSwipe(string id, [FromBody] UiSwipeRequest req,
        [FromServices] IDeviceFleet? fleet, [FromServices] IDevicePlatforms? platforms, CancellationToken ct) {
        if (req.X1 < 0 || req.Y1 < 0 || req.X2 < 0 || req.Y2 < 0)
            return Fail(400, "coordinates must be non-negative");
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, fleet, platforms, ct);
        if (err is not null) return err;

        var r = await DeviceInputLane.RunAsync(target.Id,
            t => platform.SwipeAsync(target, req.X1, req.Y1, req.X2, req.Y2, req.DurationMs, t), ct);
        return r.Ok ? Ok(new UiActionResult(true, DeviceOutcomes.Label(r), r.Note)) : UiFailure(r.Outcome, r.Note);
    }

    [HttpPost("{id}/ui/text")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> UiText(string id, [FromBody] UiTextRequest req,
        [FromServices] IDeviceFleet? fleet, [FromServices] IDevicePlatforms? platforms, CancellationToken ct) {
        if (req.Text is not { Length: > 0 }) return Fail(400, "text required");
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, fleet, platforms, ct);
        if (err is not null) return err;

        var r = await platform.InputTextAsync(target, req.Text, ct);
        return r.Ok ? Ok(new UiActionResult(true, DeviceOutcomes.Label(r), r.Note)) : UiFailure(r.Outcome, r.Note);
    }

    [HttpPost("{id}/ui/key")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> UiKey(string id, [FromBody] UiKeyRequest req,
        [FromServices] IDeviceFleet? fleet, [FromServices] IDevicePlatforms? platforms, CancellationToken ct) {
        if (!DeviceKeyNames.TryParse(req.Key, out var key))
            return Fail(400, $"unknown key (expected one of: {string.Join(", ", DeviceKeyNames.All)})");
        (IActionResult? err, var platform, var target) = await ResolveUiAsync(id, fleet, platforms, ct);
        if (err is not null) return err;

        var r = await platform.KeyAsync(target, key, ct);
        return r.Ok ? Ok(new UiActionResult(true, DeviceOutcomes.Label(r), r.Note)) : UiFailure(r.Outcome, r.Note);
    }

    [HttpGet("{id}/live")]
    [EnableRateLimiting("fetch")]
    public async Task<IActionResult> Live(string id, [FromServices] IDeviceStatusStore? store,
        [FromServices] DeviceCaptureManager? mgr, CancellationToken ct) {
        if (await ResolveDeviceIdAsync(id, store, ct) is not { } realId) return Ok(new { found = false });
        if (mgr is null) return Ok(new { found = false });
        bool isAdmin = currentUser.Current.IsAtLeast(UserRole.Admin);
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
