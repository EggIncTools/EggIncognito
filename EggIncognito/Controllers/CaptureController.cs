using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using EggIdentity.Contract;
using EggIncognito.Capture;
using EggIncognito.Core.Services;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Capture;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Feed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/capture")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class CaptureController(
    CaptureSessionManager manager,
    IAppMode appMode,
    ICurrentUser currentUser,
    HostedCaptureOptions hostedOptions,
    ILogger<CaptureController> logger) : ApiControllerBase {
    private static readonly JsonSerializerOptions Json = JsonPresets.Web;

    private bool TryResolve([NotNullWhen(true)] out CaptureSession? session,
        [NotNullWhen(false)] out IActionResult? error) {
        session = null;
        error = null;
        if (appMode.CanCapture) {
            session = manager.GetOrCreate(CaptureSessionManager.LocalKey);
            return true;
        }

        if (!appMode.HostedCaptureEnabled)
            error = Fail(403, "capture is disabled in hosted mode");
        else if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.DiscordId))
            error = Fail(401, "log in to use hosted capture");
        else {
            session = manager.Get(currentUser.DiscordId);
            if (session is null) error = Fail(404, "no capture session; start one on /capture first");
        }

        return session is not null;
    }

    private ObjectResult? RequireHostedUser() {
        if (!appMode.HostedCaptureEnabled)
            return Fail(403, "hosted capture is not enabled");
        if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.DiscordId))
            return Fail(401, "log in to use hosted capture");
        return !currentUser.UserId.HasValue
            ? Fail(401, "log in to use hosted capture")
            : null;
    }

    private CaptureTier TierFor() =>
        currentUser.IsSupporter || currentUser.IsAtLeast(UserRole.Admin)
            ? CaptureTier.Full
            : CaptureTier.Limited;

    private ObjectResult? RequireFullTier(CaptureSession session) =>
        session.Tier == CaptureTier.Full
            ? null
            : Fail(403, "limited_capture", new { detail = "this action needs full capture access" });

    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct) {
        if (!TryResolve(out var session, out var error)) {
            (int status, object? payload) = error is ObjectResult o
                ? (o.StatusCode ?? 403, o.Value)
                : (403, (object?)new ApiError("capture unavailable", null, 403));
            Response.StatusCode = status;
            await Response.WriteAsJsonAsync(payload, ct);
            return;
        }

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        await Response.WriteAsync(":ok\n\n", ct);
        await Response.Body.FlushAsync(ct);

        var (reader, subscription) = session.Hub.Subscribe();
        using var _ = subscription;
        foreach (var f in session.Hub.Snapshot()) await WriteEvent("flow", f, ct);
        await WriteEvent("stats", session.Hub.StatsSnapshot(), ct);
        try {
            await foreach (var env in reader.ReadAllAsync(ct)) {
                object? payload = env.Kind switch {
                    "flow" => env.Flow,
                    "stats" => env.Stats,
                    "notice" => env.Event,
                    _ => null
                };
                if (payload is not null) await WriteEvent(env.Kind, payload, ct);
            }
        } catch (OperationCanceledException) {
            /* client disconnected */
        }
    }

    private async Task WriteEvent(string eventName, object payload, CancellationToken ct) {
        string data = JsonSerializer.Serialize(payload, Json);
        await Response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    [HttpGet("flows")]
    public IActionResult Flows() {
        return TryResolve(out var session, out var error) ? Ok(session.Hub.Snapshot()) : error;
    }

    [HttpGet("sensitive-keys")]
    public IActionResult SensitiveKeys() => Ok(new {
        keys = CaptureSensitiveKeys.All
    });

    [HttpGet("stats")]
    public IActionResult Stats() {
        return TryResolve(out var session, out var error) ? Ok(session.Hub.StatsSnapshot()) : error;
    }

    [HttpGet("status")]
    public IActionResult Status() {
        return TryResolve(out var session, out var error) ? Ok(session.Status) : error;
    }

    [HttpPost("start")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Start(
        [FromServices] CaptureCredentialStore? credentials, [FromServices] ICaptureCaNotifier? notifier,
        [FromServices] CaptureAddressStore? addrStore, CancellationToken ct) {
        if (appMode.CanCapture)
            return await StartSessionAsync(manager.GetOrCreate(CaptureSessionManager.LocalKey), "local");
        if (!appMode.HostedCaptureEnabled)
            return Fail(403, "capture is disabled in hosted mode");
        if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.DiscordId))
            return Fail(401, "log in to use hosted capture");
        if (!currentUser.UserId.HasValue)
            return Fail(401, "log in to use hosted capture");

        CaptureSession session;
        try {
            session = manager.GetOrCreate(currentUser.DiscordId, TierFor());
        } catch (CaptureCapacityException) {
            return Fail(503, "capture capacity reached; try again later");
        }

        session.ContributorUserId = currentUser.UserId.Value;

        await RestoreCaAsync(session, credentials, currentUser.UserId.Value, ct);
        var started = await StartSessionAsync(session, currentUser.DiscordId);
        if (started is not OkObjectResult { Value: CaptureStartResult result }) return started;
        if (result.FreshCa && credentials is not null)
            await PersistFreshCaAsync(session, credentials, currentUser.UserId.Value, result.RootThumbprint, ct);

        await DeliverSetupAsync(session, currentUser.DiscordId, currentUser.UserId.Value, notifier, addrStore, ct);
        return Ok(result);
    }

    private async Task<IActionResult> StartSessionAsync(CaptureSession session, string key) {
        try {
            var result = await session.StartAsync(CancellationToken.None);
            logger.LogInformation("capture start: {Key} listening on port {Port}", key, result.Port);
            return Ok(result);
        } catch (Exception ex) {
            logger.LogError(ex, "capture start: {Key} failed on port {Port}", key, session.Port);
            return Fail(500, "capture_start_failed", new { detail = ex.Message });
        }
    }

    [HttpPost("send-config")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> SendConfig(
        [FromServices] ICaptureCaNotifier? notifier, [FromServices] CaptureAddressStore? addrStore,
        CancellationToken ct) {
        if (!appMode.HostedCaptureEnabled)
            return Fail(403, "hosted capture disabled");
        if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.DiscordId))
            return Fail(401, "log in to use hosted capture");
        if (!currentUser.UserId.HasValue)
            return Fail(401, "log in to use hosted capture");
        var session = manager.Get(currentUser.DiscordId);
        if (session is null) return Fail(409, "start a capture session first");
        session.CaDmFailed = false;
        await DeliverSetupAsync(session, currentUser.DiscordId, currentUser.UserId.Value, notifier, addrStore, ct);
        return Ok(new { sent = !session.CaDmFailed });
    }

    private async Task DeliverSetupAsync(
        CaptureSession session, string discordId, Guid userId,
        ICaptureCaNotifier? notifier, CaptureAddressStore? addrStore, CancellationToken ct) {
        if (notifier is null || addrStore is null) {
            logger.LogWarning("capture setup DM: no notifier or address store registered");
            FlagDmFailed(session);
            return;
        }

        byte[] cer;
        try {
            cer = await System.IO.File.ReadAllBytesAsync(session.CaPath, ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "capture setup DM: reading CA {Path} failed", session.CaPath);
            cer = [];
        }

        if (cer.Length == 0) {
            logger.LogWarning("capture setup DM: CA {Path} is empty or missing", session.CaPath);
            FlagDmFailed(session);
            return;
        }

        var addr = await addrStore.AddrForUserAsync(hostedOptions.Ipv6Prefix, userId, ct);
        var dm = new CaptureSetupDm(discordId, cer, addr.ToString(), hostedOptions.FrontDoorPort);
        if (await notifier.SendSetupAsync(dm, ct)) return;
        logger.LogWarning("capture setup DM: notifier declined for discord {DiscordId}", discordId);
        FlagDmFailed(session);
    }

    private static void FlagDmFailed(CaptureSession session) {
        session.CaDmFailed = true;
        session.Hub.PostNotice(new CaptureEvent(
            "caDmFailed", "Could not DM your setup; use the card below.",
            DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
    }

    private static async Task RestoreCaAsync(
        CaptureSession session, CaptureCredentialStore? store, Guid userId, CancellationToken ct) {
        if (store is null) return;
        string pfxPath = SessionPfxPath(session);
        if (System.IO.File.Exists(pfxPath)) return;
        var ca = await store.GetCaAsync(userId, ct);
        if (ca is null || ca.Pfx is []) return;
        Directory.CreateDirectory(Path.GetDirectoryName(pfxPath)!);
        await System.IO.File.WriteAllBytesAsync(pfxPath, ca.Pfx, ct);
    }

    private static async Task PersistFreshCaAsync(
        CaptureSession session, CaptureCredentialStore store, Guid userId,
        string? thumbprint, CancellationToken ct) {
        string pfxPath = SessionPfxPath(session);
        if (!System.IO.File.Exists(pfxPath)) return;
        byte[] pfx = await System.IO.File.ReadAllBytesAsync(pfxPath, ct);
        await store.SaveCaAsync(userId, pfx, thumbprint ?? "", ct);
    }

    private static string SessionPfxPath(CaptureSession session) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(session.CaPath))!, ".ca", "root.pfx");

    [HttpPost("stop")]
    public async Task<IActionResult> Stop() {
        if (!TryResolve(out var session, out var error)) return error;
        await session.StopAsync();
        return Ok(new { running = false });
    }

    [HttpPost("pause")]
    public IActionResult Pause() {
        if (!TryResolve(out var session, out var error)) return error;
        session.Hub.Paused = true;
        return Ok(new { paused = true });
    }

    [HttpPost("resume")]
    public IActionResult Resume() {
        if (!TryResolve(out var session, out var error)) return error;
        session.Hub.Paused = false;
        return Ok(new { paused = false });
    }

    [HttpPost("clear")]
    public IActionResult Clear() {
        if (!TryResolve(out var session, out var error)) return error;
        session.Hub.Clear();
        return Ok(new { cleared = true });
    }

    [HttpPost("save-endpoint")]
    public async Task<IActionResult> SaveEndpoint([FromBody] SaveFlowRequest body,
        [FromServices] IRouteCatalog routes, [FromServices] EggIncognitoDbContext? db,
        [FromServices] ConfigChangeNotifier? notifier) {
        if (!TryResolve(out var session, out var error)) return error;
        if (RequireFullTier(session) is { } no) return no;
        var flow = session.Hub.Find(body.Id);
        if (flow is null) return Fail(404, $"flow {body.Id} not in buffer");

        if (appMode.CanCapture) {
            string? path = session.SaveEndpoint(flow.Path, flow.Method, flow.Status, flow.RequestDataB64,
                flow.ResponseB64);
            if (path is null)
                return Fail(409, "capture not running or flow could not be decoded");
            session.Hub.MarkSaved(body.Id);
            return Ok(new { saved = path });
        }

        if (!currentUser.IsSupporter && !currentUser.IsAtLeast(UserRole.Contributor))
            return Fail(403, "supporter or contributor role required to save endpoints");
        if (db is null) return Fail(503, "no database configured");
        if (routes.Resolve(flow.Path) is null) return Fail(400, $"unknown route {flow.Path}");
        var decoded = session.Decode(flow.Path, flow.ResponseB64);
        if (decoded.Json is null || decoded.Type is null)
            return Fail(409, "flow could not be decoded");

        string json = PeriodicalsSanitizer.ScrubPlayerScope(decoded.Type, decoded.Json);
        var existing = await db.StoredEndpoints
            .FirstOrDefaultAsync(e => e.Path == flow.Path && e.Eid == null);
        string? previousJson = existing?.ResponseJson;
        if (existing is null) {
            db.StoredEndpoints.Add(new StoredEndpoint {
                Path = flow.Path,
                Eid = null,
                ResponseJson = json,
                ResponseType = decoded.Type,
                OwnerUserId = currentUser.UserId
            });
        } else {
            existing.ResponseJson = json;
            existing.ResponseType = decoded.Type;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
        notifier?.OnEndpointWritten(flow.Path, json, previousJson);
        session.Hub.MarkSaved(body.Id);
        return Ok(new { saved = flow.Path, store = "db" });
    }

    [HttpGet("har")]
    public IActionResult Har() {
        if (!TryResolve(out var session, out var error)) return error;
        if (RequireFullTier(session) is { } no) return no;
        byte[] bytes = Encoding.UTF8.GetBytes(session.CurrentHar());
        return File(bytes, "application/json", "capture-session.har");
    }

    [HttpGet("decode")]
    public IActionResult Decode([FromQuery] string path, [FromQuery] string responseB64) {
        if (!TryResolve(out var session, out var error)) return error;
        if (!session.AllowsDetail(path))
            return Fail(403, "limited_capture", new { detail = $"{path} is not decodable here" });
        var r = session.Decode(path, responseB64);
        return Ok(new { responseJson = r.Json, responseType = r.Type, known = r.Known });
    }

    [HttpGet("proxy-address")]
    [RequiresDb]
    public async Task<IActionResult> ProxyAddress([FromServices] CaptureAddressStore store, CancellationToken ct) {
        if (RequireHostedUser() is { } no) return no;
        var addr = await store.AddrForUserAsync(hostedOptions.Ipv6Prefix, currentUser.UserId!.Value, ct);
        return Ok(new { host = addr.ToString(), port = hostedOptions.FrontDoorPort, address = addr.ToString() });
    }

    [HttpPost("proxy-address/rotate")]
    [RequiresDb]
    public async Task<IActionResult> RotateProxyAddress([FromServices] CaptureAddressStore store,
        CancellationToken ct) {
        if (RequireHostedUser() is { } no) return no;
        var addr = await store.RotateAsync(hostedOptions.Ipv6Prefix, currentUser.UserId!.Value, ct);
        return Ok(new { host = addr.ToString(), port = hostedOptions.FrontDoorPort, address = addr.ToString() });
    }

    [HttpGet("ca.cer")]
    public async Task<IActionResult> DownloadCa([FromServices] CaptureCredentialStore? credentials,
        CancellationToken ct) {
        if (appMode.CanCapture) {
            var local = manager.GetOrCreate(CaptureSessionManager.LocalKey);
            if (!System.IO.File.Exists(local.CaPath))
                return Fail(404, "no CA yet; start capture first");
            byte[] localCer = await System.IO.File.ReadAllBytesAsync(local.CaPath, ct);
            return File(localCer, "application/x-x509-ca-cert", "eggincognito-ca.cer");
        }

        if (RequireHostedUser() is { } no) return no;
        var session = manager.Get(currentUser.DiscordId!);
        if (session is not null && System.IO.File.Exists(session.CaPath)) {
            byte[] cer = await System.IO.File.ReadAllBytesAsync(session.CaPath, ct);
            return File(cer, "application/x-x509-ca-cert", "eggincognito-ca.cer");
        }

        if (credentials is null) return Fail(503, "no database configured");
        var ca = await credentials.GetCaAsync(currentUser.UserId!.Value, ct);
        if (ca is null || ca.Pfx is [])
            return Fail(404, "no CA yet; start a capture session first");
        try {
            using var cert = X509CertificateLoader.LoadPkcs12(ca.Pfx, null);
            return File(cert.Export(X509ContentType.Cert), "application/x-x509-ca-cert", "eggincognito-ca.cer");
        } catch (CryptographicException) {
            return Fail(500, "stored CA could not be read");
        }
    }
}
