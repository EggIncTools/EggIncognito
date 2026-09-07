using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route(BridgeRoutes.Root)]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class DeviceBridgeController(
    DeviceTransportConfig config,
    ICurrentUser currentUser,
    ILogger<DeviceBridgeController> logger,
    IProcessRunner runner,
    IServiceProvider services) : ControllerBase {
    private const int StreamChunk = 64 * 1024;
    private static readonly TimeSpan ReachTimeout = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions SpecJson = new(JsonSerializerDefaults.Web);

    [HttpPost(BridgeRoutes.Exec)]
    [DisableRateLimiting]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> Exec(CancellationToken ct) {
        if (BridgeGate.Check(HttpContext, config, currentUser, logger) is { } gate) return gate;

        (IActionResult? error, var plan) = await PlanAsync(ct);
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
        if (BridgeGate.Check(HttpContext, config, currentUser, logger) is { } gate) return gate;

        (IActionResult? error, var plan) = await PlanAsync(ct);
        if (plan is null) return error!;

        Note("exec/stream", plan.Spec.Exe);
        ProcessHandle handle;
        try {
            handle = await runner.StartAsync(plan.Spec.Exe, plan.Args, HttpContext.RequestAborted);
        } catch (NotSupportedException ex) {
            plan.Dispose();
            return StatusCode(501, new { error = ex.Message });
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

    [HttpGet(BridgeRoutes.Docker + "/{**path}")]
    [HttpPost(BridgeRoutes.Docker + "/{**path}")]
    [HttpPut(BridgeRoutes.Docker + "/{**path}")]
    [HttpDelete(BridgeRoutes.Docker + "/{**path}")]
    [HttpHead(BridgeRoutes.Docker + "/{**path}")]
    [HttpPatch(BridgeRoutes.Docker + "/{**path}")]
    [DisableRateLimiting]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Docker([FromRoute] string path) {
        if (BridgeGate.Check(HttpContext, config, currentUser, logger) is { } gate) return gate;
        if (Service<DockerSocketProxy>() is not { Available: true } proxy)
            return StatusCode(503, new { error = "docker socket not available" });

        Note("docker", $"{Request.Method} {path}");
        await proxy.ForwardAsync(HttpContext, path, HttpContext.RequestAborted);
        return new EmptyResult();
    }

    [HttpGet(BridgeRoutes.Host)]
    [DisableRateLimiting]
    public async Task<IActionResult> Host(CancellationToken ct) {
        if (BridgeGate.Check(HttpContext, config, currentUser, logger) is { } gate) return gate;
        if (Service<IHostFacts>() is not { } facts)
            return StatusCode(503, new { error = "host facts are not available here" });

        Note("host", "facts");
        var result = await facts.GetAsync(ct);
        if (!result.Ok || result.Value is not { } value)
            return StatusCode(503, new { error = result.Note ?? "host facts unavailable" });

        return Ok(value);
    }

    [HttpPost(BridgeRoutes.AdbRestart)]
    [DisableRateLimiting]
    public async Task<IActionResult> AdbRestart(CancellationToken ct) {
        if (BridgeGate.Check(HttpContext, config, currentUser, logger) is { } gate) return gate;
        if (Service<IAdbServer>() is not { } adb) return StatusCode(503, new { error = "no adb server here" });

        Note("host/adb-restart", adb.Socket);
        await adb.RestartAsync(ct);
        return Ok(new { ok = true, note = adb.Describe() });
    }

    [HttpGet(BridgeRoutes.Reach)]
    [DisableRateLimiting]
    public async Task<IActionResult> Reach([FromQuery] string? host, [FromQuery] int port, CancellationToken ct) {
        if (BridgeGate.Check(HttpContext, config, currentUser, logger) is { } gate) return gate;
        if (string.IsNullOrWhiteSpace(host)) return BadRequest(new { error = "host required" });
        if (port is < 1 or > 65535) return BadRequest(new { error = "port must be between 1 and 65535" });

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

    [HttpPost(BridgeRoutes.Claim + "/{deviceId}")]
    [DisableRateLimiting]
    public async Task<IActionResult> Claim(string deviceId, [FromBody] BridgeClaimRequest? req, CancellationToken ct) {
        if (BridgeGate.Check(HttpContext, config, currentUser, logger) is { } gate) return gate;
        if (Service<IDeviceFleet>() is not { } fleet || Service<DeviceClaimRegistry>() is not { } claims)
            return StatusCode(503, new { error = "device transport not configured" });

        var enabled = await fleet.EnabledAsync(ct);
        if (enabled.All(d => !string.Equals(d.Id, deviceId, StringComparison.Ordinal)))
            return NotFound(new { error = "unknown device" });

        Note("claim", deviceId);
        var expires = claims.Claim(deviceId, TimeSpan.FromSeconds(req?.TtlSeconds ?? config.ClaimTtlSeconds));
        return Ok(new BridgeClaimResult(true, expires));
    }

    [HttpPost(BridgeRoutes.Release + "/{deviceId}")]
    [DisableRateLimiting]
    public IActionResult Release(string deviceId) {
        if (BridgeGate.Check(HttpContext, config, currentUser, logger) is { } gate) return gate;
        if (Service<DeviceClaimRegistry>() is not { } claims)
            return StatusCode(503, new { error = "device transport not configured" });

        Note("release", deviceId);
        claims.Release(deviceId);
        return Ok(new { ok = true });
    }

    private T? Service<T>() where T : class => services.GetService(typeof(T)) as T;

    private void Note(string verb, string what) =>
        logger.LogInformation("device bridge {Verb} {What} from {Caller}", verb, what,
            HttpContext.Connection.RemoteIpAddress);

    private async Task<(IActionResult? Error, BridgeExecPlan? Plan)> PlanAsync(CancellationToken ct) {
        if (!Request.HasFormContentType) return (BadRequest(new { error = "multipart/form-data required" }), null);

        var form = await Request.ReadFormAsync(ct);
        if (!form.TryGetValue(BridgeExecParts.Spec, out var raw) || raw.ToString() is not { Length: > 0 } json)
            return (BadRequest(new { error = $"the {BridgeExecParts.Spec} part is required" }), null);

        BridgeExecSpec? spec;
        try {
            spec = JsonSerializer.Deserialize<BridgeExecSpec>(json, SpecJson);
        } catch (JsonException ex) {
            return (BadRequest(new { error = $"malformed {BridgeExecParts.Spec}: {ex.Message}" }), null);
        }

        if (spec is null || string.IsNullOrEmpty(spec.Exe)) return (BadRequest(new { error = "exe required" }), null);
        if (!config.BridgeExecutables.Contains(spec.Exe, StringComparer.Ordinal))
            return (BadRequest(new { error = $"{spec.Exe} is not in DeviceTransport:BridgeExecutables" }), null);

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
                return (BadRequest(new { error = bad }), null);
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
