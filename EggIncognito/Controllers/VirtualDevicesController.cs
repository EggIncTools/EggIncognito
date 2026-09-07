using System.Globalization;
using EggIdentity.Settings.Store;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Config;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/devices/virtual")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("write")]
public sealed class VirtualDevicesController(
    IServiceProvider services,
    VirtualDeviceConfig config,
    ModuleFetcher moduleFetcher,
    IntegrityAssets integrityAssets,
    VirtualDeviceLifecycle lifecycle) : ControllerBase {
    private ProvisionedInstanceStore? Store =>
        services.GetService(typeof(ProvisionedInstanceStore)) as ProvisionedInstanceStore;

    private DeviceModuleStore? Modules =>
        services.GetService(typeof(DeviceModuleStore)) as DeviceModuleStore;

    private DeviceCaptureManager? Captures =>
        services.GetService(typeof(DeviceCaptureManager)) as DeviceCaptureManager;

    private IImageBuildExecutor? Images =>
        services.GetService(typeof(IImageBuildExecutor)) as IImageBuildExecutor;

    private ImageBuildRunner? BuildRunner =>
        services.GetService(typeof(ImageBuildRunner)) as ImageBuildRunner;

    private ImageBuildStore? BuildStore =>
        services.GetService(typeof(ImageBuildStore)) as ImageBuildStore;

    private DeviceTimelineCache? Timeline =>
        services.GetService(typeof(DeviceTimelineCache)) as DeviceTimelineCache;

    private SettingsStore? Settings =>
        services.GetService(typeof(SettingsStore)) as SettingsStore;

    private SettingsAdminService? SettingsAdmin =>
        services.GetService(typeof(SettingsAdminService)) as SettingsAdminService;

    [HttpGet]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> List(CancellationToken ct) {
        var listed = await lifecycle.Provisioner.ListAsync(ct);
        var containers = new Dictionary<string, ProvisionedInstance>(StringComparer.Ordinal);
        foreach (var c in listed.Value ?? []) containers[c.InstanceId] = c;

        List<VirtualInstanceRow> rows;
        if (lifecycle.Delegated) {
            rows = await WithActivityAsync([.. containers.Values.Select(Row)], ct);
        } else {
            if (Store is not { } store) {
                return StatusCode(503, new {
                    error = "provisioning is not registered here; it needs a database and the real device stack"
                });
            }

            rows = await WithActivityAsync([.. (await store.AllAsync(ct)).Select(r => Row(r, containers))], ct);
        }

        var byState = rows.GroupBy(r => r.State, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return Ok(new VirtualDevicesStatus(
            config.Enabled,
            listed.Ok,
            config.Kind,
            config.Image,
            config.MaxInstances,
            rows.Count(r => ProvisionStates.IsLive(r.State)),
            listed.Note ?? lifecycle.SupportNote,
            byState,
            rows));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] VirtualCreateRequest? request, CancellationToken ct) {
        if (!config.Enabled) return StatusCode(503, new { error = "virtual devices are disabled" });
        var res = await lifecycle.CreateAsync(request?.Image, ct);
        var payload = new VirtualActionResult(
            res.Ok, DeviceOutcomes.Label(res.Outcome), res.Value?.InstanceId, res.Note);
        return res.Ok ? Ok(payload) : StatusCode(res.Outcome == DeviceOutcome.Unsupported ? 503 : 400, payload);
    }

    [HttpDelete("{instanceId}")]
    public async Task<IActionResult> Destroy(string instanceId, CancellationToken ct) {
        var res = await lifecycle.DestroyAsync(instanceId, ct);
        var payload = new VirtualActionResult(res.Ok, DeviceOutcomes.Label(res.Outcome), instanceId, res.Note);
        return res.Ok ? Ok(payload) : StatusCode(res.Outcome == DeviceOutcome.Unsupported ? 503 : 409, payload);
    }

    [HttpPost("reconcile")]
    public async Task<IActionResult> Reconcile(CancellationToken ct) {
        int touched = await lifecycle.ReconcileAsync(ct);
        return Ok(new VirtualActionResult(true, DeviceOutcomes.Ok, null, $"reconciled {touched} instance(s)"));
    }

    [HttpGet("modules")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> ListModules(CancellationToken ct) {
        if (Modules is not { } store) return StatusCode(503, new { error = "no database configured" });
        var rows = (await store.ListAsync(ct))
            .Select(m => new ModuleCacheRow(m.Name, m.Version, m.ByteSize, m.Source, true, null))
            .ToList();
        return Ok(rows);
    }

    [HttpPost("modules/refresh")]
    public async Task<IActionResult> RefreshModules(CancellationToken ct) {
        var rows = new List<ModuleCacheRow>();
        foreach (var spec in config.IntegrityModules) {
            var res = await moduleFetcher.ResolveAsync(spec, true, ct);
            rows.Add(new ModuleCacheRow(res.Name, res.Version, res.ByteSize, spec.Repo ?? spec.Url ?? "",
                res.FromCache, res.Error));
        }

        return Ok(rows);
    }

    [HttpGet("integrity")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Integrity(CancellationToken ct) =>
        Ok(IntegrityView(await integrityAssets.PeekAsync(ct)));

    [HttpPost("integrity/refresh")]
    public async Task<IActionResult> RefreshIntegrity(CancellationToken ct) =>
        Ok(IntegrityView(await integrityAssets.ResolveAsync(true, ct)));

    [HttpGet("images")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> ListImages(CancellationToken ct) {
        if (Images is not { } images) return StatusCode(503, new { error = "image builds are not registered here" });
        string? active = await ActiveOverrideAsync(ct);
        var listed = await images.ListAsync("redroid/redroid:*", ct);
        var rows = new List<ImageRow>();
        foreach (var img in listed.Value ?? []) {
            string? tag = img.RepoTags.Count > 0 ? img.RepoTags[0] : null;
            bool isActive = active is { Length: > 0 } && img.RepoTags.Contains(active, StringComparer.Ordinal);
            rows.Add(new ImageRow(tag, img.RepoTags, img.Id, img.Size, img.Created, isActive));
        }

        return Ok(new ImagesView(
            config.Build.Enabled, active, config.Image, listed.Ok, listed.Note, rows));
    }

    [HttpPost("images/build")]
    public async Task<IActionResult> BuildImage([FromBody] ImageBuildRequest? request, CancellationToken ct) {
        if (!config.Build.Enabled)
            return StatusCode(503, new { error = "image builds are disabled (Devices:Virtual:Build:Enabled is false)" });
        if (BuildRunner is not { } runner) return StatusCode(503, new { error = "no database configured" });
        if (request is null) return BadRequest(new { error = "a build spec is required" });

        string? baseImage = request.BaseImage;
        if (request.Integrity && string.IsNullOrWhiteSpace(baseImage) && !request.Magisk)
            baseImage = await ActiveOverrideAsync(ct) ?? config.Image;

        var spec = new ImageBuildSpec(
            string.IsNullOrWhiteSpace(request.AndroidVersion) ? "11.0.0" : request.AndroidVersion,
            request.Gapps, request.Magisk, request.Ndk, baseImage, Integrity: request.Integrity);
        var started = await runner.StartAsync(spec, ct);
        return started.Ok ? Ok(started) : StatusCode(409, started);
    }

    [HttpGet("images/build/{id:long}")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> BuildStatus(long id, CancellationToken ct) {
        if (BuildStore is not { } store) return StatusCode(503, new { error = "no database configured" });
        var row = await store.GetAsync(id, ct);
        if (row is null) return NotFound(new { error = $"unknown build {id}" });
        return Ok(new ImageBuildStatusView(
            row.Id, row.Spec, row.Tag, row.State, row.Note, row.Log, row.StartedAt, row.FinishedAt));
    }

    [HttpPost("images/use")]
    public async Task<IActionResult> UseImage([FromBody] ImageUseRequest? request, CancellationToken ct) {
        if (SettingsAdmin is not { } admin) return StatusCode(503, new { error = "no database configured" });
        if (request?.Tag is not { Length: > 0 } tag) return BadRequest(new { error = "a tag is required" });

        var saved = await admin.SaveAsync(SettingKeys.VirtualImageOverride, tag, User.Identity?.Name, ct);
        if (!saved.Ok) return BadRequest(new VirtualActionResult(false, DeviceOutcomes.Error, tag, saved.Error));
        return Ok(new VirtualActionResult(true, DeviceOutcomes.Ok, null, $"active image set to {tag}"));
    }

    [HttpPost("images/remove")]
    public async Task<IActionResult> RemoveImage([FromBody] ImageRemoveRequest? request, CancellationToken ct) {
        if (Images is not { } images) return StatusCode(503, new { error = "image builds are not registered here" });
        if (request?.Tag is not { Length: > 0 } tag) return BadRequest(new { error = "a tag is required" });

        var removed = await images.RemoveAsync(tag, ct);
        if (removed.Ok && Settings is { } settings && SettingsAdmin is { } admin) {
            var active = await settings.GetAsync(SettingKeys.VirtualImageOverride, ct);
            if (string.Equals(active?.Value, tag, StringComparison.Ordinal))
                await admin.SaveAsync(SettingKeys.VirtualImageOverride, null, User.Identity?.Name, ct);
        }

        var payload = new VirtualActionResult(removed.Ok, DeviceOutcomes.Label(removed.Outcome), tag, removed.Note);
        return removed.Ok ? Ok(payload) : StatusCode(removed.Outcome == DeviceOutcome.Unsupported ? 503 : 400, payload);
    }

    private async Task<string?> ActiveOverrideAsync(CancellationToken ct) {
        if (Settings is not { } settings) return null;
        string? value = (await settings.GetAsync(SettingKeys.VirtualImageOverride, ct))?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IntegrityAssetsView IntegrityView(IntegrityBundle b) => new(
        b.Ok, b.Error, b.Profile?.Model, b.Profile?.Product, b.Profile?.Fingerprint,
        Day(b.Profile?.ReleasedOn), Day(b.Profile?.Expiry), b.PatchDate,
        b.KeyboxSource, b.KeyboxSerials.Count, b.KeyboxNote,
        [.. b.Modules.Select(m => $"{m.Spec.Name} {m.ModuleId} {m.Version ?? "?"}")],
        b.Warnings);

    private static string? Day(DateOnly? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<List<VirtualInstanceRow>> WithActivityAsync(
        List<VirtualInstanceRow> rows, CancellationToken ct) {
        if (Timeline is not { } timeline) return rows;
        List<string> deviceIds = [.. rows.Select(r => r.DeviceId).OfType<string>()];
        if (deviceIds.Count == 0) return rows;

        var running = await timeline.RunningAsync(deviceIds, ct);
        if (running.Count == 0) return rows;

        var byDevice = running
            .GroupBy(j => j.DeviceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(j => j.Id).First(), StringComparer.Ordinal);

        return [.. rows.Select(r => r.DeviceId is { } id && byDevice.TryGetValue(id, out var job)
            ? r with { Activity = ActivityLine(job) }
            : r)];
    }

    private static string ActivityLine(DeviceJobRow job) =>
        job.Message is { Length: > 0 } message ? $"{job.Kind}: {message}" : $"{job.Kind} running";

    private VirtualInstanceRow Row(
        ProvisionedInstanceRow row, Dictionary<string, ProvisionedInstance> containers) {
        containers.TryGetValue(row.InstanceId, out var container);
        (long flows, string? lastFlow) = Flows(row.DeviceId);
        return new VirtualInstanceRow(
            row.InstanceId, row.Kind, row.Image, row.State,
            row.AdbSerial, row.DeviceId, row.CreatedAt, row.LastSeenAt, row.Note,
            container is not null, container?.Note, flows, lastFlow);
    }

    private VirtualInstanceRow Row(ProvisionedInstance instance) {
        (long flows, string? lastFlow) = Flows(instance.DeviceId);
        return new VirtualInstanceRow(
            instance.InstanceId, instance.Kind, instance.Image, instance.State,
            instance.AdbSerial, instance.DeviceId, instance.CreatedAt, null, instance.Note,
            true, instance.Note, flows, lastFlow);
    }

    private (long Flows, string? LastFlow) Flows(string? deviceId) {
        if (deviceId is not { Length: > 0 } id || Captures is not { } captures) return (0, null);
        var snap = captures.HubFor(id)?.Snapshot();
        return (captures.DiagFor(id).Flows, snap is { Count: > 0 } ? snap[^1].Timestamp : null);
    }
}
