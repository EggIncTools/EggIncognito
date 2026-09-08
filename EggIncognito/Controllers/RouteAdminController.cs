using EggIncognito.Core.Services;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Routes;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Routes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/admin/routes")]
[ApiAccess(ApiAccessLevel.Contributor)]
[EnableRateLimiting("write")]
public sealed class RouteAdminController(
    IRouteCatalog routes,
    IRouteCatalogReport report,
    IProtoReflection proto,
    ICurrentUser currentUser,
    IServiceProvider services) : ControllerBase {
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    private IRouteOverrideProvider? Overrides =>
        services.GetService(typeof(IRouteOverrideProvider)) as IRouteOverrideProvider;

    [HttpGet]
    [EnableRateLimiting("read")]
    public IActionResult List() => Ok(report.Rows());

    [HttpGet("binary")]
    [EnableRateLimiting("read")]
    public IActionResult ListBinary() {
        var binary = report.Binary();
        if (binary is null) return StatusCode(503, new { error = "no database configured" });
        return Ok(binary);
    }

    [HttpPost("binary/refresh")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> RefreshBinaryAsync(CancellationToken ct) {
        if (Db is null) return StatusCode(503, new { error = "no database configured" });
        if (services.GetService(typeof(EndpointCatalogRebuilder)) is not EndpointCatalogRebuilder rebuilder)
            return StatusCode(503, new { error = "no database configured" });

        return Ok(await rebuilder.RebuildAsync(ct));
    }

    [HttpPut("{**path}")]
    public async Task<IActionResult> UpsertAsync(string path, [FromBody] UpsertRouteOverride body) {
        if (routes.Resolve(path) is null) return NotFound(new { error = $"unknown route {path}" });
        if (body.Request is null && body.Response is null && body.RequestWrapped is null
            && body.ResponseWrapped is null && body.PathParam is null) {
            return BadRequest(new { error = "all fields null, use DELETE to remove an override" });
        }

        if (body.Request is not null && proto.FindMessage(body.Request) is null)
            return BadRequest(new { error = $"unknown proto type {body.Request}" });
        if (body.Response is not null && proto.FindMessage(body.Response) is null)
            return BadRequest(new { error = $"unknown proto type {body.Response}" });

        var db = Db;
        var provider = Overrides;
        if (db is null || provider is null) return StatusCode(503, new { error = "no database configured" });

        var now = DateTimeOffset.UtcNow;
        var existing = await db.RouteOverrides.FirstOrDefaultAsync(o => o.Path == path);
        if (existing is null) {
            db.RouteOverrides.Add(new RouteOverride {
                Path = path,
                RequestType = body.Request,
                ResponseType = body.Response,
                RequestWrapped = body.RequestWrapped,
                ResponseWrapped = body.ResponseWrapped,
                PathParam = body.PathParam,
                UpdatedAt = now,
                UpdatedBy = currentUser.UserId
            });
        } else {
            existing.RequestType = body.Request;
            existing.ResponseType = body.Response;
            existing.RequestWrapped = body.RequestWrapped;
            existing.ResponseWrapped = body.ResponseWrapped;
            existing.PathParam = body.PathParam;
            existing.UpdatedAt = now;
            existing.UpdatedBy = currentUser.UserId;
        }

        await db.SaveChangesAsync();
        provider.Invalidate();

        var effective = routes.Resolve(path)!;
        return Ok(new RouteUpsertResult(effective.Path, EffectiveInfo.From(effective)));
    }

    [HttpDelete("{**path}")]
    public async Task<IActionResult> DeleteAsync(string path) {
        var db = Db;
        var provider = Overrides;
        if (db is null || provider is null) return StatusCode(503, new { error = "no database configured" });

        var existing = await db.RouteOverrides.FirstOrDefaultAsync(o => o.Path == path);
        if (existing is null) return NotFound(new { error = $"no override for {path}" });
        db.RouteOverrides.Remove(existing);
        await db.SaveChangesAsync();
        provider.Invalidate();
        return Ok(new RouteDeleteResult(path));
    }
}
