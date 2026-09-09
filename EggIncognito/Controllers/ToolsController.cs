using System.Data.Common;
using System.Text;
using EggIncognito.Capture;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Services;
using EggIncognito.Models.Tools;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/tools")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("read")]
public sealed class ToolsController(
    IConfiguration config,
    IProtoReflection reflection,
    ILogger<ToolsController> logger) : ControllerBase {
    private string Root => ContentRoot.Resolve(config["ContentRoot"]);
    private string YamlPath => Path.Combine(Root, "RouteMap", "routes.yaml");
    private string DefaultsDir => Path.Combine(Root, "Endpoints", "default");
    private string CapturePath => config["CapturePath"] ?? Path.Combine(Root, "captures");

    [HttpGet("live-version")]
    public IActionResult LiveVersion([FromQuery] string platform = "ios") {
        var v = new LiveVersionStore(CapturePath).Latest(platform);
        if (v is null) return Ok(new LiveVersionResult(false));
        return Ok(new LiveVersionResult(true, v.Platform, v.Version, v.Build, v.ClientVersion, v.LastSeen));
    }

    [HttpGet("postman-collection")]
    public IActionResult PostmanCollection() {
        string json = PostmanBundle.BuildJson(YamlPath);
        return File(Encoding.UTF8.GetBytes(json), "application/json", "EggIncognito.postman_collection.json");
    }

    [HttpPost("decode")]
    public IActionResult Decode([FromBody] DecodeRequest body) {
        var r = BlobDecoder.Decode(body.Base64 ?? "");
        return Ok(new BlobDecodeResult(r.Type, r.Json, r.Wrapped, r.Confidence));
    }

    [HttpGet("endpoint-status")]
    public IActionResult Status() {
        var r = EndpointStatus.Classify(YamlPath, DefaultsDir);
        return Ok(new EndpointStatusResult(r.Ok, r.Empty, r.Missing));
    }

    [HttpGet("boost-costs")]
    public IActionResult BoostCosts() {
        string path = Path.Combine(DefaultsDir, "ei", "get_config.json");
        if (!System.IO.File.Exists(path)) return NotFound(new ToolError("no get_config capture"));
        try {
            string json = System.IO.File.ReadAllText(path);
            var costs = BoostCostExtractor.FromConfigJson(json);
            return Ok(new BoostCostsResult(
                costs.Count,
                [.. costs.Select(kv => new BoostCostRow(kv.Key, kv.Value.Price, kv.Value.TokenPrice, kv.Value.SeRequired))]));
        } catch (Exception ex) {
            return Ok(new BoostCostsResult(0, Error: ex.Message));
        }
    }

    [HttpGet("colleggtibles")]
    public IActionResult Colleggtibles() {
        string path = Path.Combine(DefaultsDir, "ei", "get_periodicals.json");
        if (!System.IO.File.Exists(path)) return NotFound(new ToolError("no get_periodicals capture"));
        try {
            string json = System.IO.File.ReadAllText(path);
            var extract = ColleggtibleExtractor.FromPeriodicalsJson(json);
            return Ok(new ColleggtiblesResult(
                extract.Eggs.Count,
                [.. extract.Eggs.Select(e => new ColleggtibleRow(e.Identifier, e.Dimension, e.TierValues))],
                extract.ContractEggMap));
        } catch (Exception ex) {
            return Ok(new ColleggtiblesResult(0, Error: ex.Message));
        }
    }

    [HttpPost("extract-ios-proto")]
    public IActionResult ExtractIosProto([FromBody] ExtractIosProtoRequest body) {
        byte[] macho;
        try {
            macho = Convert.FromBase64String(body.BinaryBase64 ?? "");
        } catch {
            return ExtractFailed("input is not valid base64");
        }

        return ExtractResultJson(DescriptorProtoCarver.Extract(macho));
    }

    [HttpPost("extract-proto")]
    [RequestSizeLimit(200_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 200_000_000)]
    public async Task<IActionResult> ExtractProto(IFormFile binary, IFormFile? meta, [FromForm] string? fileName,
        CancellationToken ct) {
        if (binary is null || binary.Length == 0) return ExtractFailed("no binary uploaded");

        byte[] bin = await ReadFormFileAsync(binary, ct);
        byte[]? metaBytes = meta is { Length: > 0 } ? await ReadFormFileAsync(meta, ct) : null;

        var r = DescriptorProtoCarver.Extract(bin);
        if (r.Ok) {
            (string? appVersion, string? build) = AppMetaReader.Read(metaBytes);
            r = r with { AppVersion = appVersion, Build = build };
            await RecordAnalyzedAsync(bin, r, fileName ?? binary.FileName, ct);
        }

        return ExtractResultJson(r, AnalyzedFileStore.Sha256Hex(bin));
    }

    private static async Task<byte[]> ReadFormFileAsync(IFormFile file, CancellationToken ct) {
        byte[] bytes = new byte[file.Length];
        using var dest = new MemoryStream(bytes);
        await file.CopyToAsync(dest, ct);
        return bytes;
    }

    private async Task RecordAnalyzedAsync(byte[] bytes, DescriptorProtoCarver.ExtractResult r, string? fileName,
        CancellationToken ct) {
        var store = HttpContext.RequestServices.GetService<AnalyzedFileStore>();
        if (store is null) return;
        try {
            await store.RecordAsync(new AnalyzedFileStore.Entry(
                AnalyzedFileStore.Sha256Hex(bytes), "analyze", null, r.ProtoSha, r.AppVersion, r.Build,
                r.ClientVersion?.ToString(), fileName), ct);
        } catch (DbException ex) {
            logger.LogWarning(ex, "tools: analyzed-file record for {FileName} not persisted", fileName);
        }
    }

    private OkObjectResult ExtractFailed(string diagnostics) =>
        Ok(new ProtoExtractResult(false, null, diagnostics, null, []));

    private OkObjectResult ExtractResultJson(DescriptorProtoCarver.ExtractResult r, string? fileSha = null) =>
        Ok(new ProtoExtractResult(
            r.Ok, r.Proto, r.Diagnostics, r.ProtoSha, r.Messages, r.AppVersion, r.Build, r.ClientVersion, fileSha));

    [HttpPost("diagnose")]
    public IActionResult Diagnose([FromBody] DiagnoseRequest body) {
        byte[] bytes;
        try {
            bytes = ProtoFraming.FromBase64Loose(body.Base64 ?? "");
        } catch {
            return Ok(new ToolError("input is not valid base64"));
        }

        byte[] inner = ProtoFraming.TryUnwrap(bytes) ?? bytes;
        return Ok(WireForensics.Diagnose(inner, body.RootType, reflection));
    }
}
