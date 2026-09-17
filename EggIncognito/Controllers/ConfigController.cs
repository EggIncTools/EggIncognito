using System.Text;
using EggIdentity.Contract;
using EggIncognito.Core.Services;
using EggIncognito.Models.Config;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Feed;
using Ei;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/config")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class ConfigController(
    GameConfigStore store,
    ITransportPipeline pipeline,
    IHttpClientFactory httpFactory,
    ICurrentUser currentUser,
    IAppMode appMode,
    IConfiguration config,
    ConfigChangeNotifier notifier,
    DataCatalog catalog,
    ILogger<ConfigController> logger) : ControllerBase {
    private const string GetConfigUrl = AuxbrainHosts.Origin + "/ei/get_config";

    [HttpGet]
    public IActionResult List() {
        if (!store.Enabled) return Ok(new { enabled = false, configs = Array.Empty<object>() });
        var configs = store.List().Select(c => new { platform = c.Platform, savedAt = c.SavedAt, bytes = c.Bytes });
        return Ok(new { enabled = true, configs });
    }

    [HttpGet("{platform}")]
    public IActionResult Get(string platform) {
        var c = store.Get(platform);
        return c is null
            ? NotFound(new { error = "no stored config for that platform" })
            : File(Encoding.UTF8.GetBytes(c.Json), "application/json", $"{platform}-config.json");
    }

    [HttpPost("{platform}/ingest")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Ingest(string platform, [FromBody] IngestRequest body, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        byte[] bytes;
        try {
            bytes = ProtoFraming.FromBase64Loose(body.ConfigResponseBase64 ?? "");
        } catch (Exception ex) {
            return Ok(new { ok = false, diagnostics = $"not valid base64: {ex.Message}" });
        }

        var cfg = BestConfig(bytes);
        return cfg is null
            ? Ok(new { ok = false, diagnostics = "could not parse as a ConfigResponse (wrapped or direct)" })
            : await StoreAsync(platform, cfg, ct);
    }

    private ConfigResponse? BestConfig(byte[] bytes) {
        ConfigResponse? Try(byte[] b) {
            try {
                return ConfigResponse.Parser.ParseFrom(b);
            } catch {
                return null;
            }
        }

        int Shells(ConfigResponse? c) {
            return c?.DlcCatalog?.Shells.Count ?? 0;
        }

        var direct = Try(bytes);
        ConfigResponse? unwrapped = null;
        byte[]? inner = ProtoFraming.TryUnwrap(bytes);
        if (inner is not null) unwrapped = Try(inner);

        var best = Shells(unwrapped) > Shells(direct) ? unwrapped : direct;
        best ??= unwrapped ?? direct;
        logger.LogInformation("config ingest: direct={D} shells, unwrapped={U} shells -> chose {C}",
            Shells(direct), Shells(unwrapped), ReferenceEquals(best, unwrapped) ? "unwrapped" : "direct");
        return best;
    }

    [HttpPost("{platform}/ingest-json")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> IngestJson(string platform, [FromBody] IngestJsonRequest body,
        CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        int jsonLen = body.Json?.Length ?? 0;
        logger.LogInformation("config ingest-json: {Platform} received {Len} chars", platform, jsonLen);
        ConfigResponse cfg;
        try {
            cfg = ConfigResponse.Parser.ParseJson(body.Json ?? "");
        } catch (Exception ex) {
            logger.LogWarning(ex, "config ingest-json: {Platform} ParseJson failed", platform);
            return Ok(new { ok = false, diagnostics = $"could not parse ConfigResponse JSON ({jsonLen} chars): {ex.Message}" });
        }

        logger.LogInformation("config ingest-json: {Platform} parsed, dlcCatalog={Has}, shells={Shells}",
            platform, cfg.DlcCatalog is not null, cfg.DlcCatalog?.Shells.Count ?? 0);
        return await StoreAsync(platform, cfg, ct);
    }

    [HttpPost("{platform}/refresh-live")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("egress")]
    public async Task<IActionResult>
        RefreshLive(string platform, [FromBody] RefreshRequest? body, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        string? salt = string.IsNullOrEmpty(body?.Salt) ? null : body.Salt;
        if (salt is null && !pipeline.CanSign)
            return StatusCode(503,
                new { error = "live refresh needs a signing salt; ingest a captured config instead" });

        var req = new ConfigRequest { Rinfo = new BasicRequestInfo { Platform = platform } };
        var built = salt is null
            ? pipeline.Build(req.ToByteArray(), true)
            : pipeline.Build(req.ToByteArray(), true, salt);

        string rawBody;
        try {
            var client = httpFactory.CreateClient("inspector");
            var content = new StringContent(built.FinalFormBody, Encoding.UTF8, "application/x-www-form-urlencoded");
            var resp = await client.PostAsync(GetConfigUrl, content, ct);
            rawBody = (await resp.Content.ReadAsStringAsync(ct)).Trim();
            if (!resp.IsSuccessStatusCode)
                return Ok(new { ok = false, diagnostics = $"get_config -> HTTP {(int)resp.StatusCode}" });
        } catch (Exception ex) {
            logger.LogWarning(ex, "config refresh-live failed");
            return Ok(new { ok = false, diagnostics = ex.Message });
        }

        ConfigResponse cfg;
        try {
            byte[] raw = ProtoFraming.FromBase64Loose(rawBody);
            byte[] inner = ProtoFraming.TryUnwrap(raw) ?? raw;
            cfg = ConfigResponse.Parser.ParseFrom(inner);
        } catch (Exception ex) {
            return Ok(new { ok = false, diagnostics = $"could not decode get_config: {ex.Message}" });
        }

        return await StoreAsync(platform, cfg, ct);
    }

    [HttpPost("refresh-endpoints")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("egress")]
    public async Task<IActionResult> RefreshEndpoints([FromBody] RefreshEndpointsRequest? body, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (!appMode.CanWrite) return StatusCode(403, new { error = "endpoint writes disabled in this mode" });
        string? salt = string.IsNullOrEmpty(body?.Salt) ? null : body.Salt;
        if (salt is null && !pipeline.CanSign)
            return StatusCode(503,
                new { error = "live refresh needs a signing salt; ingest a captured response instead" });

        string? platform = string.IsNullOrEmpty(body?.Platform) ? "IOS" : body.Platform;

        string contentRoot = ContentRoot.Resolve(config["ContentRoot"]);
        var extractor = EndpointExtractor.ForRepo(contentRoot, null, "EI0000000000000000", true);
        extractor.Quiet = true;
        extractor.LiveRoutes = new HashSet<string>(catalog.FeedWireRoutes(), StringComparer.Ordinal);
        extractor.WriteObserver = notifier;

        async Task<object> One(string url, string label, byte[] inner) {
            (bool ok, string raw, string diag) = await FetchRawAsync(url, inner, salt, ct);
            if (!ok) return new { endpoint = label, ok = false, diagnostics = diag };
            string? written;
            try {
                written = extractor.ForceWriteEndpoint(url, "POST", 200, null, raw);
            } catch (Exception ex) {
                return new { endpoint = label, ok = false, diagnostics = $"write failed: {ex.Message}" };
            }

            return new { endpoint = label, ok = written is not null, path = written };
        }

        var results = new List<object>();
        foreach (var src in catalog.EgressSources()) {
            if (src.BuildEgressRequest is null) continue;
            results.Add(await One(
                DataCatalog.EgressUrl(src), src.WireRoute!, src.BuildEgressRequest(platform)));
        }

        return Ok(new { results });
    }

    private async Task<(bool Ok, string Raw, string Diag)> FetchRawAsync(string url, byte[] inner, string? salt,
        CancellationToken ct) {
        var built = salt is null ? pipeline.Build(inner, true) : pipeline.Build(inner, true, salt);
        try {
            var client = httpFactory.CreateClient("inspector");
            var content = new StringContent(built.FinalFormBody, Encoding.UTF8, "application/x-www-form-urlencoded");
            var resp = await client.PostAsync(url, content, ct);
            string raw = (await resp.Content.ReadAsStringAsync(ct)).Trim();
            if (!resp.IsSuccessStatusCode) return (false, "", $"HTTP {(int)resp.StatusCode}");
            return (true, raw, "ok");
        } catch (Exception ex) {
            logger.LogWarning(ex, "live fetch {Url} failed", url);
            return (false, "", ex.Message);
        }
    }

    private async Task<IActionResult> StoreAsync(string platform, ConfigResponse cfg, CancellationToken ct) {
        if (!store.Enabled)
            return StatusCode(503,
                new { error = "config store needs ConfigStore:Dir or ShipAssets:OutputDir configured" });

        string? json = JsonFormatter.Default.Format(cfg);
        await store.SaveAsync(platform, json, ct);
        int shells = cfg.DlcCatalog?.Shells.Count ?? 0;
        int shellObjects = cfg.DlcCatalog?.ShellObjects.Count ?? 0;
        logger.LogInformation("config stored: {Platform} ({Bytes}B, {Shells} shells)", platform, json.Length, shells);
        return Ok(new {
            ok = true,
            platform,
            bytes = json.Length,
            hasDlcCatalog = cfg.DlcCatalog is not null,
            shells,
            shellObjects
        });
    }

    private ObjectResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin)
            ? null
            : StatusCode(403, new { error = "admin role required" });
}
