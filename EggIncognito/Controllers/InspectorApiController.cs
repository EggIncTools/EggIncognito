using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using EggIncognito.Capture;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Inspector;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Inspector;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/inspector")]
[ApiAccess(ApiAccessLevel.Public)]
#pragma warning disable S107
public sealed class InspectorApiController(
    IProtoReflection reflection,
    ITransportPipeline pipeline,
    IHttpClientFactory httpFactory,
    IAppMode appMode,
    ICurrentUser currentUser,
    ISealedProxy sealedProxy,
    ILogger<InspectorApiController> logger) : ControllerBase
#pragma warning restore S107
{
    [HttpGet("messages")]
    public IActionResult Messages() {
        Response.Headers.CacheControl = "private, max-age=300";
        return Ok(reflection.AllMessageTypeNames());
    }

    [HttpGet("message-roles")]
    public IActionResult Roles([FromServices] IMessageRoleIndex roles) {
        Response.Headers.CacheControl = "private, max-age=60";
        return Ok(roles.Snapshot());
    }

    [HttpGet("rinfo-seed")]
    [ApiAccess(ApiAccessLevel.Public)]
    public async Task<IActionResult> RinfoSeed(
        [FromServices] IServiceProvider services,
        [FromServices] IConfiguration configuration,
        CancellationToken ct) {
        var seed = await RegistrySeedAsync(services, ct) ?? CapturedSeed(configuration) ?? EmptySeed;
        Response.Headers.CacheControl = "private, max-age=60";
        return Ok(seed);
    }

    private static readonly RinfoSeedResponse EmptySeed = new("", "", "", "", "", "", "", false);

    private static async Task<RinfoSeedResponse?> RegistrySeedAsync(IServiceProvider services, CancellationToken ct) {
        if (services.GetService(typeof(ProtoRegistryStore)) is not ProtoRegistryStore store) return null;

        List<ProtoVersion> rows;
        try {
            rows = await store.ListAsync(null, ct);
        } catch (DbException) {
            return null;
        }

        var newest = ProtoVersionOrdering.Latest(rows, r => new VersionKey(
            r.Platform, r.AppVersion, r.Build, r.ClientVersion, null, r.DetectedAt.UtcDateTime));
        return newest is null
            ? null
            : EmptySeed with {
                ClientVersion = newest.ClientVersion ?? "",
                Version = newest.AppVersion,
                Build = newest.Build,
                Platform = ProtoPlatformName(newest.Platform)
            };
    }

    private static RinfoSeedResponse? CapturedSeed(IConfiguration configuration) {
        string capturePath = configuration["CapturePath"]
                             ?? Path.Combine(ContentRoot.Resolve(configuration["ContentRoot"]), "captures");
        var store = new LiveVersionStore(capturePath);
        var v = store.Latest(Platforms.Ios) ?? store.Latest(Platforms.Android);
        return v is null
            ? null
            : EmptySeed with {
                ClientVersion = v.ClientVersion?.ToString(CultureInfo.InvariantCulture) ?? "",
                Version = v.Version ?? "",
                Build = v.Build ?? "",
                Platform = ProtoPlatformName(v.Platform)
            };
    }

    private static string ProtoPlatformName(string? platform) =>
        Platforms.Matches(platform, Platforms.Android) ? "DROID"
        : Platforms.Matches(platform, Platforms.Ios) ? "IOS"
        : "";

    [HttpPost("build")]
    public IActionResult Build([FromBody] BuildRequest body) {
        var parser = reflection.FindParser(body.RequestType);
        var descriptor = reflection.FindMessage(body.RequestType);
        if (parser is null || descriptor is null)
            throw new ApiException(
                $"unknown request type '{body.RequestType}'",
                "Type not found in the compiled proto. Check the endpoint's request type in routes.yaml.");

        IMessage message;
        try {
            string fieldsJson = body.Fields?.GetRawText() ?? "{}";
            string merged = MergeEnv(fieldsJson, body.Env, descriptor);
            message = parser.ParseJson(merged);
        } catch (Exception ex) {
            throw new ApiException(
                $"invalid request JSON: {ex.Message}",
                $"Field values do not match {body.RequestType}. Check field types in the schema panel.");
        }

        byte[]? inner = message.ToByteArray();

        var result = pipeline.Build(inner, body.Wrap, body.Salt);

        return Ok(new {
            result.Stages,
            result.FinalBase64,
            result.FinalFormBody,
            canSign = !string.IsNullOrEmpty(body.Salt)
        });
    }

    [HttpPost("send")]
    [EnableRateLimiting("egress")]
    public async Task<IActionResult> Send([FromBody] SendRequest body) {
        if (appMode.Mode == AppMode.Hosted && !currentUser.IsAuthenticated)
            throw new ApiException(
                "log in to use Live API from the hosted site",
                "Sign in with Discord, then retry. Local runs are never gated.",
                StatusCodes.Status403Forbidden);

        bool useSealed = body.Sealed;
        if (useSealed && !await sealedProxy.CanUseAsync(currentUser, HttpContext.RequestAborted))
            throw new ApiException(
                "the sealed API proxy is a supporter perk",
                "Become a supporter and enable it, or send without sealed mode.",
                StatusCodes.Status403Forbidden);

        var uri = ResolveAllowedUrl(ComposeUrl(body), Request.Host.Host);

        var client = useSealed ? sealedProxy.CreateEgressClient() : httpFactory.CreateClient("inspector");
        var content = new StringContent(body.FormBody,
            Encoding.UTF8, "application/x-www-form-urlencoded");

        HttpResponseMessage resp;
        try {
            resp = await client.PostAsync(uri, content);
        } catch (Exception ex) {
            logger.LogWarning(ex, "send {Host}{Path} -> FAILED", uri.Host, uri.AbsolutePath);

            return Ok(new ApiError(
                $"send failed: {ex.Message}",
                "Check the target host is reachable and the URL is correct.",
                StatusCodes.Status502BadGateway));
        }

        logger.LogInformation("send {Host}{Path} -> HTTP {Status}",
            uri.Host, uri.AbsolutePath, (int)resp.StatusCode);

        string raw = (await resp.Content.ReadAsStringAsync()).Trim();
        var parser = body.ResponseType is not null
            ? reflection.FindParser(body.ResponseType)
            : null;

        var decode = pipeline.Decode(raw, parser, body.ResponseWrapped);
        return Ok(new {
            status = (int)resp.StatusCode,
            rawBase64 = raw,
            decode.Stages,
            json = decode.Json,
            error = decode.Error,
            resolution = decode.Error is null ? null : DecodeResolution(decode.Error),
            wrappedMismatch = decode.WrappedMismatch
        });
    }

    private static string DecodeResolution(string error) =>
        error.StartsWith("no parser", StringComparison.Ordinal)
            ? "No response type declared for this endpoint. Set `response:` in routes.yaml."
            : "The declared shape did not decode. Check the response type and the responseWrapped flag.";

    [HttpPost("decode-response")]
    public IActionResult DecodeResponse([FromBody] DecodeResponseRequest body) {
        var parser = body.ResponseType is not null ? reflection.FindParser(body.ResponseType) : null;
        var decode = pipeline.Decode(body.RawBase64, parser, body.ResponseWrapped);
        return Ok(new { decode.Stages, json = decode.Json, error = decode.Error, wrappedMismatch = decode.WrappedMismatch });
    }

    private static string MergeEnv(string fieldsJson, JsonElement? env,
        MessageDescriptor descriptor) {
        var rinfoField = descriptor.Fields.InFieldNumberOrder()
            .FirstOrDefault(f => f.Name == "rinfo");
        if (env is null || rinfoField is null) return fieldsJson;

        using var fieldsDoc = JsonDocument.Parse(fieldsJson);
        var root = fieldsDoc.RootElement;

        var outObj = new Dictionary<string, JsonElement>();
        foreach (var prop in root.EnumerateObject())
            outObj[prop.Name] = prop.Value.Clone();

        string? rinfoKey = rinfoField.JsonName;
        var envObj = new Dictionary<string, JsonElement>();
        foreach (var prop in env.Value.EnumerateObject())
            envObj[prop.Name] = prop.Value.Clone();

        if (root.TryGetProperty(rinfoKey, out var existingRinfo) && existingRinfo.ValueKind == JsonValueKind.Object) {
            foreach (var prop in existingRinfo.EnumerateObject())
                envObj[prop.Name] = prop.Value.Clone();
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) {
            writer.WriteStartObject();
            foreach (var kv in outObj) {
                if (kv.Key == rinfoKey) continue;
                writer.WritePropertyName(kv.Key);
                kv.Value.WriteTo(writer);
            }

            writer.WritePropertyName(rinfoKey);
            writer.WriteStartObject();
            foreach (var kv in envObj) {
                writer.WritePropertyName(kv.Key);
                kv.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ComposeUrl(SendRequest body) {
        if (!string.IsNullOrWhiteSpace(body.Url)) return body.Url;

        string path = (body.Path ?? "").TrimStart('/');
        if (path.Length == 0)
            throw new ApiException(
                "no target for this send",
                "Send needs either an absolute url or an endpoint path.");

        string url = $"{AuxbrainHosts.OriginForPath(path)}/{path}";
        string param = (body.PathParam ?? "").Trim();
        return param.Length == 0 ? url : url + "/" + Uri.EscapeDataString(param);
    }

    private static Uri ResolveAllowedUrl(string url, string selfHost) {
        return !Uri.TryCreate(url, UriKind.Absolute, out var parsed)
               || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            ? throw new ApiException(
                $"invalid target URL '{url}'",
                "URL must be an absolute http(s) URL.")
            : IsAllowedHost(parsed.Host, selfHost)
                ? parsed
                : throw new ApiException(
                    $"target URL host '{parsed.Host}' is not allowed",
                    "Allowed hosts: this instance, localhost, 127.0.0.1, *.auxbrain.com, auxbrainhome.appspot.com, and its <service>-dot-auxbrainhome.appspot.com subdomains.");
    }

    internal static bool IsAllowedHost(string host, string? selfHost = null) =>
        host is "localhost" or "127.0.0.1"
        || (!string.IsNullOrEmpty(selfHost) && string.Equals(host, selfHost, StringComparison.OrdinalIgnoreCase))
        || AuxbrainHosts.IsAuxbrain(host);
}
