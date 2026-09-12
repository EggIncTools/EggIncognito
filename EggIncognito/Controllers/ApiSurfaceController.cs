using EggIncognito.Models.Docs;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[EnableRateLimiting("read")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class ApiSurfaceController(AuxbrainSurface surface) : ControllerBase {
    private const string LandingTemplate = """
                                       <!doctype html>
                                       <html lang="en">
                                       <head>
                                       <meta charset="utf-8">
                                       <meta name="viewport" content="width=device-width, initial-scale=1">
                                       <title>EggIncognito API</title>
                                       <style>
                                       body { font-family: ui-monospace, Consolas, monospace; background: #16161a; color: #d8d8d8;
                                              max-width: 46rem; margin: 3rem auto; padding: 0 1rem; line-height: 1.55; }
                                       a { color: #fb923c; }
                                       code { background: #26262c; padding: 0.1rem 0.35rem; border-radius: 3px; }
                                       h1 { font-size: 1.35rem; }
                                       li { margin: 0.35rem 0; }
                                       </style>
                                       </head>
                                       <body>
                                       <h1>EggIncognito</h1>
                                       <p>Drop-in mock of the Egg Inc (auxbrain) API. Point your client's base URL here and every
                                       route answers with canned, deterministic data.</p>
                                       <p>Requests are <code>POST</code> with an <code>application/x-www-form-urlencoded</code>
                                       body of <code>data=&lt;base64 protobuf&gt;</code>. Signing is optional here: the mock
                                       accepts unsigned requests. The real API requires the request wrapped in a signed
                                       <code>AuthenticatedMessage</code> on wrapped routes; the catalog and OpenAPI document mark
                                       which routes those are.</p>
                                       <ul>
                                       <li><a href="/api/openapi.json">/api/openapi.json</a> OpenAPI 3.0 document</li>
                                       <li><a href="/api/reference">/api/reference</a> browsable reference (Redoc)</li>
                                       <li><a href="/api/catalog">/api/catalog</a> machine-readable route catalog</li>
                                       <li><a href="/protos#api">/protos#api</a> build, sign, send, and decode requests</li>
                                       <li><a href="/docs">/docs</a> message and endpoint documentation</li>
                                       </ul>
                                       <p>Namespace indexes: NAMESPACE_LINKS</p>
                                       </body>
                                       </html>
                                       """;

    private const string ReferenceHtml = """
                                         <!doctype html>
                                         <html lang="en">
                                         <head>
                                         <meta charset="utf-8">
                                         <meta name="viewport" content="width=device-width, initial-scale=1">
                                         <title>EggIncognito API reference</title>
                                         <style>body { margin: 0; padding: 0; }</style>
                                         </head>
                                         <body>
                                         <redoc spec-url="/api/openapi.json"></redoc>
                                         <script src="https://cdn.jsdelivr.net/npm/redoc@2/bundles/redoc.standalone.js"></script>
                                         </body>
                                         </html>
                                         """;

    [HttpGet("/api")]
    public ContentResult Landing() {
        string links = string.Join(" ", surface.Namespaces.Order(StringComparer.Ordinal)
            .Select(ns => $"<a href=\"/{ns}\">/{ns}</a>"));
        return Content(LandingTemplate.Replace("NAMESPACE_LINKS", links), "text/html");
    }

    [HttpGet("/api/openapi.json")]
    public ContentResult OpenApi() {
        Response.Headers.CacheControl = "public, max-age=300";
        return Content(surface.OpenApiJson, "application/json");
    }

    [HttpGet("/api/reference")]
    public ContentResult Reference() => Content(ReferenceHtml, "text/html");

    [HttpGet("/api/catalog")]
    public IActionResult Catalog() {
        Response.Headers.CacheControl = "public, max-age=300";
        return Ok(surface.Entries.Select(AuxbrainRouteWire.From));
    }

    [HttpGet("/{ns:eins}")]
    public IActionResult NamespaceIndex(string ns) {
        if (!surface.Namespaces.Contains(ns)) return NotFound();
        Response.Headers.CacheControl = "public, max-age=300";
        List<AuxbrainRouteWire> routes =
            [.. surface.Entries.Where(e => e.Namespace == ns).Select(AuxbrainRouteWire.From)];
        return Ok(new AuxbrainNamespaceIndex(ns, routes));
    }
}
