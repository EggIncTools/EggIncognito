using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/console")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("read")]
public sealed class ApiConsoleController(IApiDescriptionGroupCollectionProvider explorer) : ApiControllerBase {
    [HttpGet("endpoints")]
    public IActionResult Endpoints() {
        var endpoints = explorer.ApiDescriptionGroups.Items
            .SelectMany(g => g.Items)
            .Where(d => d.RelativePath is not null &&
                        d.RelativePath.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            .Where(d => !d.RelativePath!.StartsWith("api/console", StringComparison.OrdinalIgnoreCase))
            .Select(d => new {
                method = d.HttpMethod ?? "GET",
                route = "/" + d.RelativePath,
                query = d.ParameterDescriptions
                    .Where(p => p.Source.Id is "Query" or "Path")
                    .Select(p => new { name = p.Name, source = p.Source.Id, type = TypeName(p.Type), required = p.IsRequired })
                    .ToList(),
                hasBody = d.ParameterDescriptions.Any(p => p.Source.Id == "Body"),
                bodyType = d.ParameterDescriptions.FirstOrDefault(p => p.Source.Id == "Body") is { } b
                    ? TypeName(b.Type)
                    : null,
                hasFile = d.ParameterDescriptions.Any(p => p.Source.Id == "FormFile"
                                                           || (p.Type is not null &&
                                                               typeof(IFormFile).IsAssignableFrom(p.Type)))
            })
            .OrderBy(e => e.route, StringComparer.Ordinal)
            .ThenBy(e => e.method, StringComparer.Ordinal)
            .ToList();

        return Ok(new { count = endpoints.Count, endpoints });
    }

    private static string TypeName(Type? t) {
        if (t is null) return "string";
        var u = Nullable.GetUnderlyingType(t) ?? t;
        return u.Name;
    }
}
