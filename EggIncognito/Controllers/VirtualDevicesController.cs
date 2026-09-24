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
    VirtualDeviceConfig config,
    ModuleFetcher moduleFetcher,
    IntegrityAssets integrityAssets,
    VirtualDeviceLifecycle lifecycle) : ApiControllerBase {
    [HttpGet]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> List([FromServices] ProvisionedInstanceStore? store,
        [FromServices] DeviceCaptureManager? captures, [FromServices] DeviceTimelineCache? timeline,
        CancellationToken ct) {
        var listed = await lifecycle.Provisioner.ListAsync(ct);
        var containers = new Dictionary<string, ProvisionedInstance>(StringComparer.Ordinal);
        foreach (var c in listed.Value ?? []) containers[c.InstanceId] = c;

        List<VirtualInstanceRow> rows;
        if (lifecycle.Delegated) {
            rows = await WithActivityAsync(timeline, [.. containers.Values.Select(c => Row(c, captures))], ct);
        } else {
            if (store is null) {
                return Fail(503, "provisioning is not registered here; it needs a database and the real device stack");
            }

            rows = await WithActivityAsync(timeline,
                [.. (await store.AllAsync(ct)).Select(r => Row(r, containers, captures))], ct);
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
        if (!config.Enabled) return Fail(503, "virtual devices are disabled");
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
    [RequiresDb]
    public async Task<IActionResult> ListModules([FromServices] DeviceModuleStore modules, CancellationToken ct) {
        var rows = (await modules.ListAsync(ct))
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
    [Requires<IImageBuildExecutor>("image builds are not registered here")]
    public async Task<IActionResult> ListImages([FromServices] IImageBuildExecutor images,
        [FromServices] SettingsStore? settings, CancellationToken ct) {
        string? active = await ActiveOverrideAsync(settings, ct);
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
    public async Task<IActionResult> BuildImage([FromBody] ImageBuildRequest? request,
        [FromServices] ImageBuildRunner? runner, [FromServices] SettingsStore? settings, CancellationToken ct) {
        if (!config.Build.Enabled)
            return Fail(503, "image builds are disabled (Devices:Virtual:Build:Enabled is false)");
        if (runner is null) return Fail(503, "no database configured");
        if (request is null) return Fail(400, "a build spec is required");

        string? baseImage = request.BaseImage;
        if (request.Integrity && string.IsNullOrWhiteSpace(baseImage) && !request.Magisk)
            baseImage = await ActiveOverrideAsync(settings, ct) ?? config.Image;

        var spec = new ImageBuildSpec(
            string.IsNullOrWhiteSpace(request.AndroidVersion) ? "11.0.0" : request.AndroidVersion,
            request.Gapps, request.Magisk, request.Ndk, baseImage, Integrity: request.Integrity);
        var started = await runner.StartAsync(spec, ct);
        return started.Ok ? Ok(started) : StatusCode(409, started);
    }

    [HttpGet("images/build/{id:long}")]
    [EnableRateLimiting("read")]
    [RequiresDb]
    public async Task<IActionResult> BuildStatus(long id, [FromServices] ImageBuildStore store,
        CancellationToken ct) {
        var row = await store.GetAsync(id, ct);
        if (row is null) return Fail(404, $"unknown build {id}");
        return Ok(new ImageBuildStatusView(
            row.Id, row.Spec, row.Tag, row.State, row.Note, row.Log, row.StartedAt, row.FinishedAt));
    }

    [HttpPost("images/use")]
    [RequiresDb]
    public async Task<IActionResult> UseImage([FromBody] ImageUseRequest? request,
        [FromServices] SettingsAdminService admin, CancellationToken ct) {
        if (request?.Tag is not { Length: > 0 } tag) return Fail(400, "a tag is required");

        var saved = await admin.SaveAsync(SettingKeys.VirtualImageOverride, tag, User.Identity?.Name, ct);
        if (!saved.Ok) return BadRequest(new VirtualActionResult(false, DeviceOutcomes.Error, tag, saved.Error));
        return Ok(new VirtualActionResult(true, DeviceOutcomes.Ok, null, $"active image set to {tag}"));
    }

    [HttpPost("images/remove")]
    [Requires<IImageBuildExecutor>("image builds are not registered here")]
    public async Task<IActionResult> RemoveImage([FromBody] ImageRemoveRequest? request,
        [FromServices] IImageBuildExecutor images, [FromServices] SettingsStore? settings,
        [FromServices] SettingsAdminService? settingsAdmin, CancellationToken ct) {
        if (request?.Tag is not { Length: > 0 } tag) return Fail(400, "a tag is required");

        var removed = await images.RemoveAsync(tag, ct);
        if (removed.Ok && settings is { } s && settingsAdmin is { } admin) {
            var active = await s.GetAsync(SettingKeys.VirtualImageOverride, ct);
            if (string.Equals(active?.Value, tag, StringComparison.Ordinal))
                await admin.SaveAsync(SettingKeys.VirtualImageOverride, null, User.Identity?.Name, ct);
        }

        var payload = new VirtualActionResult(removed.Ok, DeviceOutcomes.Label(removed.Outcome), tag, removed.Note);
        return removed.Ok ? Ok(payload) : StatusCode(removed.Outcome == DeviceOutcome.Unsupported ? 503 : 400, payload);
    }

    private static async Task<string?> ActiveOverrideAsync(SettingsStore? settings, CancellationToken ct) {
        if (settings is null) return null;
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

    private static async Task<List<VirtualInstanceRow>> WithActivityAsync(
        DeviceTimelineCache? timeline, List<VirtualInstanceRow> rows, CancellationToken ct) {
        if (timeline is null) return rows;
        List<string> deviceIds = [.. rows.Select(r => r.DeviceId).OfType<string>()];
        if (deviceIds.Count == 0) return rows;

        var running = await timeline.RunningAsync(deviceIds, ct);
        if (running.Count == 0) return rows;

        var byDevice = running
            .GroupBy(j => j.DeviceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.MaxBy(j => j.Id)!, StringComparer.Ordinal);

        return [.. rows.Select(r => r.DeviceId is { } id && byDevice.TryGetValue(id, out var job)
            ? r with { Activity = ActivityLine(job) }
            : r)];
    }

    private static string ActivityLine(DeviceJobRow job) =>
        job.Message is { Length: > 0 } message ? $"{job.Kind}: {message}" : $"{job.Kind} running";

    private static VirtualInstanceRow Row(
        ProvisionedInstanceRow row, Dictionary<string, ProvisionedInstance> containers, DeviceCaptureManager? captures) {
        containers.TryGetValue(row.InstanceId, out var container);
        (long flows, string? lastFlow) = Flows(captures, row.DeviceId);
        return new VirtualInstanceRow(
            row.InstanceId, row.Kind, row.Image, row.State,
            row.AdbSerial, row.DeviceId, row.CreatedAt, row.LastSeenAt, row.Note,
            container is not null, container?.Note, flows, lastFlow);
    }

    private static VirtualInstanceRow Row(ProvisionedInstance instance, DeviceCaptureManager? captures) {
        (long flows, string? lastFlow) = Flows(captures, instance.DeviceId);
        return new VirtualInstanceRow(
            instance.InstanceId, instance.Kind, instance.Image, instance.State,
            instance.AdbSerial, instance.DeviceId, instance.CreatedAt, null, instance.Note,
            true, instance.Note, flows, lastFlow);
    }

    private static (long Flows, string? LastFlow) Flows(DeviceCaptureManager? captures, string? deviceId) {
        if (deviceId is not { Length: > 0 } id || captures is null) return (0, null);
        var snap = captures.HubFor(id)?.Snapshot();
        return (captures.DiagFor(id).Flows, snap is { Count: > 0 } ? snap[^1].Timestamp : null);
    }
}
