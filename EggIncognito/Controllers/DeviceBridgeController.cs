using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route(BridgeRoutes.Root)]
[ApiAccess(ApiAccessLevel.Public)]
[BridgeGate]
public sealed class DeviceBridgeController(
    DeviceTransportConfig config,
    ILogger<DeviceBridgeController> logger,
    IProcessRunner runner) : ApiControllerBase {
    private const int StreamChunk = 64 * 1024;
    private static readonly TimeSpan ReachTimeout = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions SpecJson = new(JsonSerializerDefaults.Web);

    [HttpPost(BridgeRoutes.Exec)]
    [DisableRateLimiting]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> Exec(CancellationToken ct) {
        (var error, var plan) = await PlanAsync(ct);
        if (plan is null) return error!;

        Note("exec", plan.Spec.Exe);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(plan.TimeoutMs);
        try {
            return Ok(await RunAsync(plan, timeout.Token));
        } finally {
            plan.Dispose();
        }
    }

    private async Task<BridgeExecResult> RunAsync(BridgeExecPlan plan, CancellationToken ct) {
        if (plan.Spec.BinaryStdout) {
            var bytes = await runner.RunBytesAsync(plan.Spec.Exe, plan.Args, ct);
            return new BridgeExecResult(bytes.ExitCode, null, Convert.ToBase64String(bytes.Stdout), bytes.Stderr,
                plan.CollectOutputs());
        }

        var text = await runner.RunAsync(plan.Spec.Exe, plan.Args, ct);
        return new BridgeExecResult(text.ExitCode, text.Stdout, null, text.Stderr, plan.CollectOutputs());
    }

    [HttpPost(BridgeRoutes.ExecStream)]
    [DisableRateLimiting]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> ExecStream(CancellationToken ct) {
        (IActionResult? error, var plan) = await PlanAsync(ct);
        if (plan is null) return error!;

        Note("exec/stream", plan.Spec.Exe);
        ProcessHandle handle;
        try {
            handle = await runner.StartAsync(plan.Spec.Exe, plan.Args, HttpContext.RequestAborted);
        } catch (NotSupportedException ex) {
            plan.Dispose();
            return Fail(501, ex.Message);
        }

        try {
            await PumpAsync(handle, HttpContext.RequestAborted);
        } catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) {
            Note("exec/stream aborted", ex.Message);
        } finally {
            await handle.DisposeAsync();
            plan.Dispose();
        }

        return new EmptyResult();
    }

    private async Task PumpAsync(ProcessHandle handle, CancellationToken ct) {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/octet-stream";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var body = Response.Body;
        byte[] buffer = new byte[StreamChunk];
        int read;
        while ((read = await handle.Stdout.ReadAsync(buffer, ct)) > 0)
            await BridgeStreamFrames.WriteAsync(body, BridgeStreamFrames.Stdout, buffer.AsMemory(0, read), ct);

        int exit = await handle.Exited;
        string tail = handle.StderrTail();
        if (tail.Length > 0)
            await BridgeStreamFrames.WriteAsync(body, BridgeStreamFrames.Stderr, Encoding.UTF8.GetBytes(tail), ct);
        await BridgeStreamFrames.WriteExitAsync(body, exit, ct);
    }

    [HttpGet(BridgeRoutes.Host)]
    [DisableRateLimiting]
    public async Task<IActionResult> Host([FromServices] IHostFacts? facts, CancellationToken ct) {
        if (facts is null) return Fail(503, "host facts are not available here");

        Note("host", "facts");
        var result = await facts.GetAsync(ct);
        if (!result.Ok || result.Value is not { } value) return Fail(503, result.Note ?? "host facts unavailable");

        return Ok(value);
    }

    [HttpPost(BridgeRoutes.AdbRestart)]
    [DisableRateLimiting]
    public async Task<IActionResult> AdbRestart([FromServices] IAdbServer? adb, CancellationToken ct) {
        if (adb is null) return Fail(503, "no adb server here");

        Note("host/adb-restart", adb.Socket);
        await adb.RestartAsync(ct);
        return Ok(new { ok = true, note = adb.Describe() });
    }

    [HttpGet(BridgeRoutes.Reach)]
    [DisableRateLimiting]
    public async Task<IActionResult> Reach([FromQuery] string? host, [FromQuery] int port, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(host)) return Fail(400, "host required");
        if (port is < 1 or > 65535) return Fail(400, "port must be between 1 and 65535");

        Note("host/reach", $"{host}:{port}");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ReachTimeout);
        using var client = new TcpClient();
        try {
            await client.ConnectAsync(host, port, timeout.Token);
            return Ok(new BridgeReachResult(true, "connected"));
        } catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException
                                        or InvalidOperationException) {
            return Ok(new BridgeReachResult(false, ex.Message));
        }
    }

    [HttpGet(BridgeRoutes.Fleet)]
    [DisableRateLimiting]
    public async Task<IActionResult> Fleet(
        [FromServices] IDeviceFleet? fleet, [FromServices] DeviceProxyPusher? pusher, CancellationToken ct) {
        if (fleet is null) return Fail(503, "device fleet not configured");

        Note("fleet", "list");
        var enabled = await fleet.EnabledAsync(ct);
        return Ok(new BridgeFleet([.. enabled.Select(FleetEntry)], pusher?.HostIp));
    }

    [HttpPost(BridgeRoutes.Poke)]
    [HttpPost(BridgeRoutes.Poke + "/{deviceId}")]
    [DisableRateLimiting]
    public async Task<IActionResult> Poke(string? deviceId, [FromQuery] bool force,
        [FromServices] IDeviceAgentClient? agent, [FromServices] IDeviceFleet? fleet, CancellationToken ct) {
        if (agent is not { Enabled: true }) return Fail(503, "no device agent on this host");
        if (deviceId is { Length: > 0 } && await UnknownDeviceAsync(fleet, deviceId, ct) is { } unknown) return unknown;

        Note("poke", deviceId ?? "all");
        bool queued = await agent.PokeAsync(deviceId, force, ct);
        return queued ? Accepted(new { ok = true, queued = true }) : Fail(502, "the agent did not accept the poke");
    }

    [HttpPost(BridgeRoutes.Claim + "/{deviceId}")]
    [DisableRateLimiting]
    public async Task<IActionResult> Claim(string deviceId, [FromBody] BridgeClaimBody? req,
        [FromServices] IDeviceFleet? fleetSvc, [FromServices] DeviceClaimRegistry? claimsSvc, CancellationToken ct) {
        if (fleetSvc is not { } fleet || claimsSvc is not { } claims)
            return Fail(503, "device transport not configured");

        var enabled = await fleet.EnabledAsync(ct);
        if (enabled.All(d => !string.Equals(d.Id, deviceId, StringComparison.Ordinal)))
            return Fail(404, "unknown device");

        Note("claim", deviceId);
        var expires = claims.Claim(deviceId, TimeSpan.FromSeconds(req?.TtlSeconds ?? config.ClaimTtlSeconds));
        return Ok(new BridgeClaimOutcome(true, expires));
    }

    [HttpPost(BridgeRoutes.Release + "/{deviceId}")]
    [DisableRateLimiting]
    public IActionResult Release(string deviceId, [FromServices] DeviceClaimRegistry? claims) {
        if (claims is null) return Fail(503, "device transport not configured");

        Note("release", deviceId);
        claims.Release(deviceId);
        return Ok(new { ok = true });
    }

    [HttpPut(BridgeRoutes.Overrides + "/{deviceId}")]
    [DisableRateLimiting]
    public async Task<IActionResult> OverridesSet(string deviceId, [FromBody] DeviceResponseOverrideSet? body,
        [FromServices] DeviceResponseOverrideStore? store, [FromServices] IDeviceFleet? fleet, CancellationToken ct) {
        if (store is null) return Fail(503, "no capture proxy on this host");
        if (await UnknownDeviceAsync(fleet, deviceId, ct) is { } unknown) return unknown;
        if (body?.Entries is not { Count: > 0 } entries) return Fail(400, "at least one override entry is required");

        Note("overrides", deviceId);
        var res = await store.SetAsync(deviceId, entries, ct);
        if (!res.Ok) return Fail(400, res.Note ?? "overrides rejected");
        return Ok(new { ok = true, count = entries.Count });
    }

    [HttpDelete(BridgeRoutes.Overrides + "/{deviceId}")]
    [DisableRateLimiting]
    public async Task<IActionResult> OverridesClear(string deviceId,
        [FromServices] DeviceResponseOverrideStore? store, [FromServices] IDeviceFleet? fleet, CancellationToken ct) {
        if (store is null) return Fail(503, "no capture proxy on this host");
        if (await UnknownDeviceAsync(fleet, deviceId, ct) is { } unknown) return unknown;

        Note("overrides", deviceId);
        var res = await store.ClearAsync(deviceId, ct);
        if (!res.Ok) return Fail(400, res.Note ?? "overrides not cleared");
        return Ok(new { ok = true });
    }

    [HttpGet(BridgeRoutes.Capture + "/{deviceId}")]
    [DisableRateLimiting]
    public async Task<IActionResult> Capture(
        string deviceId, [FromServices] IDeviceCaptureHubs? hubs, CancellationToken ct) {
        if (hubs is null) return Fail(503, "no capture proxy on this host");
        if (hubs.HubFor(deviceId) is not { } hub) return Fail(404, "no capture for that device on this host");

        Note("capture", deviceId);
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/x-ndjson";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var (reader, subscription) = hub.Subscribe();
        using (subscription) {
            try {
                foreach (var f in hub.Snapshot()) await WriteEnvelopeAsync(new CaptureEnvelope("flow", f, null, null), ct);
                await WriteEnvelopeAsync(new CaptureEnvelope("stats", null, hub.StatsSnapshot(), null), ct);
                await foreach (var env in reader.ReadAllAsync(ct)) await WriteEnvelopeAsync(env, ct);
            } catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) {
                logger.LogDebug(ex, "bridge capture stream closed");
                Note("capture ended", deviceId);
            }
        }

        return new EmptyResult();
    }

    private async Task WriteEnvelopeAsync(CaptureEnvelope env, CancellationToken ct) {
        await Response.WriteAsync(JsonSerializer.Serialize(env, SpecJson) + "\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private async Task<IActionResult?> UnknownDeviceAsync(IDeviceFleet? fleet, string deviceId, CancellationToken ct) {
        if (fleet is null) return Fail(503, "device transport not configured");

        var enabled = await fleet.EnabledAsync(ct);
        return enabled.All(d => !string.Equals(d.Id, deviceId, StringComparison.Ordinal))
            ? Fail(404, "unknown device")
            : null;
    }

    private static BridgeFleetEntry FleetEntry(DeviceEntry d) =>
        new(d.Id, d.Platform, d.Label, d.Target, d.Package, d.Origin, d.CapturePort);

    private void Note(string verb, string what) =>
        logger.LogInformation("device bridge {Verb} {What} from {Caller}", verb, what,
            HttpContext.Connection.RemoteIpAddress);

    private async Task<(IActionResult? Error, BridgeExecPlan? Plan)> PlanAsync(CancellationToken ct) {
        if (!Request.HasFormContentType) return (Fail(400, "multipart/form-data required"), null);

        var form = await Request.ReadFormAsync(ct);
        if (!form.TryGetValue(BridgeExecParts.Spec, out var raw) || raw.ToString() is not { Length: > 0 } json)
            return (Fail(400, $"the {BridgeExecParts.Spec} part is required"), null);

        BridgeExecSpec? spec;
        try {
            spec = JsonSerializer.Deserialize<BridgeExecSpec>(json, SpecJson);
        } catch (JsonException ex) {
            return (Fail(400, $"malformed {BridgeExecParts.Spec}: {ex.Message}"), null);
        }

        if (spec is not { Exe.Length: > 0 }) return (Fail(400, "exe required"), null);
        if (!config.BridgeExecutables.Contains(spec.Exe, StringComparer.Ordinal))
            return (Fail(400, $"{spec.Exe} is not in DeviceTransport:BridgeExecutables"), null);

        var plan = new BridgeExecPlan(spec, Directory.CreateTempSubdirectory("egi-bridge-"));
        try {
            foreach (var part in form.Files) {
                string target = Path.Combine(plan.Dir.FullName, Sanitize(part.Name));
                await using var sink = System.IO.File.Create(target);
                await part.CopyToAsync(sink, ct);
                plan.Inputs[part.Name] = target;
            }

            if (Substitute(plan) is { } bad) {
                plan.Dispose();
                return (Fail(400, bad), null);
            }
        } catch (Exception) {
            plan.Dispose();
            throw;
        }

        return (null, plan);
    }

    private static string? Substitute(BridgeExecPlan plan) {
        var args = new List<string>();
        foreach (string arg in plan.Spec.Args ?? []) {
            if (!BridgePlaceholders.TryParse(arg, out var kind, out string name)) {
                args.Add(arg);
                continue;
            }

            if (kind == BridgePlaceholderKind.Input) {
                if (!plan.Inputs.TryGetValue(name, out string? input)) return $"no input part named {name} was sent";
                args.Add(input);
                continue;
            }

            string allocated = Path.Combine(plan.Dir.FullName, "out-" + Sanitize(name));
            plan.Outputs[name] = allocated;
            args.Add(allocated);
        }

        plan.Args = [.. args];
        return null;
    }

    private static string Sanitize(string name) {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name) sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        string cleaned = sb.ToString().Trim('.');
        return cleaned.Length == 0 ? "part" : cleaned;
    }
}
