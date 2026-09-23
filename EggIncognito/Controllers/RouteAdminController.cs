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
    ICurrentUser currentUser) : ApiControllerBase {
    [HttpGet]
    [EnableRateLimiting("read")]
    public IActionResult List() => Ok(report.Rows());

    [HttpGet("binary")]
    [EnableRateLimiting("read")]
    public IActionResult ListBinary() {
        var binary = report.Binary();
        if (binary is null) return Fail(503, "no database configured");
        return Ok(binary);
    }

    [HttpPost("binary/refresh")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    public async Task<IActionResult> RefreshBinaryAsync([FromServices] EndpointCatalogRebuilder rebuilder,
        CancellationToken ct) =>
        Ok(await rebuilder.RebuildAsync(ct));

    [HttpPut("{**path}")]
    [RequiresDb]
    [Requires<IRouteOverrideProvider>("no database configured")]
    public async Task<IActionResult> UpsertAsync(string path, [FromBody] UpsertRouteOverride body,
        [FromServices] EggIncognitoDbContext db, [FromServices] IRouteOverrideProvider provider) {
        if (routes.Resolve(path) is null) return Fail(404, $"unknown route {path}");
        if (body.Request is null && body.Response is null && body.RequestWrapped is null
            && body.ResponseWrapped is null && body.PathParam is null) {
            return Fail(400, "all fields null, use DELETE to remove an override");
        }

        if (body.Request is not null && proto.FindMessage(body.Request) is null)
            return Fail(400, $"unknown proto type {body.Request}");
        if (body.Response is not null && proto.FindMessage(body.Response) is null)
            return Fail(400, $"unknown proto type {body.Response}");

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

        if (routes.Resolve(path) is not { } effective) return Fail(500, "override saved but route resolution failed");
        return Ok(new RouteUpsertResult(effective.Path, EffectiveInfo.From(effective)));
    }

    [HttpDelete("{**path}")]
    [RequiresDb]
    [Requires<IRouteOverrideProvider>("no database configured")]
    public async Task<IActionResult> DeleteAsync(string path, [FromServices] EggIncognitoDbContext db,
        [FromServices] IRouteOverrideProvider provider) {
        var existing = await db.RouteOverrides.FirstOrDefaultAsync(o => o.Path == path);
        if (existing is null) return Fail(404, $"no override for {path}");
        db.RouteOverrides.Remove(existing);
        await db.SaveChangesAsync();
        provider.Invalidate();
        return Ok(new RouteDeleteResult(path));
    }
}
