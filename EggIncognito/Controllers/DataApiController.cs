using System.Text.Json;
using System.Text.Json.Nodes;
using EggIncognito.Core.Services;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/v1/data")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class DataApiController(DataCatalog catalog, ICurrentUser currentUser) : ControllerBase {
    [HttpGet]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Index(CancellationToken ct) {
        var roots = catalog.Sources.Where(s => s.Extends is null && s.Listed).ToList();
        var items = new List<object>(roots.Count);
        foreach (var s in roots) {
            var payload = await ProduceOf(s, ct);
            var children = new List<object>();
            foreach (var child in catalog.Children(s).Where(c => c.Listed)) {
                var childPayload = await ProduceOf(child, ct);
                children.Add(new {
                    child.Id,
                    child.Group,
                    url = catalog.UrlFor(child),
                    displayName = child.DisplayName,
                    description = child.Description,
                    provenance = child.Provenance.ToString(),
                    access = child.Access.ToString(),
                    feed = child.Feed,
                    acceptsName = child.AcceptsName,
                    bytes = childPayload?.Bytes.LongLength,
                    meta = MetaOf(child, childPayload)
                });
            }

            items.Add(new {
                s.Id,
                s.Group,
                url = catalog.UrlFor(s),
                displayName = s.DisplayName,
                description = s.Description,
                provenance = s.Provenance.ToString(),
                access = s.Access.ToString(),
                feed = s.Feed,
                acceptsName = s.AcceptsName,
                bytes = payload?.Bytes.LongLength,
                meta = MetaOf(s, payload),
                refresh = new { s.Refresh.Egress, deviceTrigger = s.Refresh.Device is not null },
                children
            });
        }

        return Ok(new { count = items.Count, sources = items });
    }

    private async Task<DataPayload?> ProduceOf(DataSource s, CancellationToken ct) {
        if (s.AcceptsName) return null;
        try {
            return await s.Produce(new DataProduceContext(HttpContext, null), ct);
        } catch {
            return null;
        }
    }

    private static object? MetaOf(DataSource s, DataPayload? payload) {
        if (s.Group != "gamedata" || payload is null) return null;
        try {
            var reader = new Utf8JsonReader(payload.Bytes);
            if (JsonNode.Parse(ref reader) is not JsonObject root) return null;
            var binaryVersion = root["binaryVersion"]?.GetValue<string>();
            var provenance = root["provenance"]?.DeepClone();
            if (binaryVersion is null && provenance is null) return null;
            return new { binaryVersion, provenance };
        } catch {
            return null;
        }
    }

    [HttpGet("{group}/{id}")]
    [EnableRateLimiting("data")]
    public async Task<IActionResult> Get(string group, string id, [FromQuery] string? name, CancellationToken ct) {
        var src = catalog.ById(group, id);
        if (src is null) return NotFound(new { error = "unknown data source", group, id });
        return src.Extends is not null
            ? NotFound(new { error = "this is an extension dataset", url = catalog.UrlFor(src) })
            : await Serve(src, name, ct);
    }

    [HttpGet("{group}/{parent}/{sub}")]
    [EnableRateLimiting("data")]
    public async Task<IActionResult> GetExtension(string group, string parent, string sub, [FromQuery] string? name,
        CancellationToken ct) {
        var src = catalog.ByChild(group, parent, sub);
        return src is null
            ? NotFound(new { error = "unknown extension dataset", group, parent, sub })
            : await Serve(src, name, ct);
    }

    private async Task<IActionResult> Serve(DataSource src, string? name, CancellationToken ct) {
        if (src.Access == DataAccess.Authenticated && !currentUser.IsAuthenticated)
            return StatusCode(401,
                new { error = "authentication required", hint = "mint an API key at /api/v1/keys or log in" });

        if (src.AcceptsName && string.IsNullOrEmpty(name))
            return BadRequest(new { error = "this source requires a name query parameter" });

        var payload = await src.Produce(new DataProduceContext(HttpContext, name), ct);
        if (payload is null) return NotFound(new { error = "data not available", id = src.Id });

        var bytes = payload.Bytes;
        if (payload.ContentType == "application/json" && Request.Query["meta"] != "1")
            bytes = StripMeta(bytes);

        var maxAge = src.Provenance == DataProvenance.Asset ? TimeSpan.FromDays(30) : TimeSpan.FromSeconds(30);
        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = maxAge };
        Response.Headers["X-Data-Source"] = src.Id;
        return File(bytes, payload.ContentType);
    }

    private static readonly JsonSerializerOptions IndentedJson = JsonPresets.Indented;

    private static byte[] StripMeta(byte[] bytes) {
        try {
            var reader = new Utf8JsonReader(bytes);
            if (JsonNode.Parse(ref reader) is not JsonObject root) return bytes;
            if (!root.ContainsKey("binaryVersion") && !root.ContainsKey("provenance")) return bytes;
            root.Remove("binaryVersion");
            root.Remove("provenance");
            return JsonSerializer.SerializeToUtf8Bytes(root, IndentedJson);
        } catch {
            return bytes;
        }
    }
}
