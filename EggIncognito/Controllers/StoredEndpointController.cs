using EggIdentity.Auth;
using EggIncognito.Core.Services;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Endpoints;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/db")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("write")]
public sealed class StoredEndpointController(ICurrentUser currentUser, TimeProvider time) : ApiControllerBase {
    [HttpPost("endpoint")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [RequiresDb]
    public async Task<IActionResult> UpsertEndpointAsync([FromBody] UpsertEndpoint body,
        [FromServices] IRouteCatalog routes, [FromServices] EggIncognitoDbContext db) {
        if (routes.Resolve(body.Path) is null) return Fail(400, $"unknown route {body.Path}");

        var existing = await db.StoredEndpoints
            .FirstOrDefaultAsync(e => e.Path == body.Path && e.Eid == body.Eid);
        if (existing is null) {
            db.StoredEndpoints.Add(new StoredEndpoint {
                Path = body.Path,
                Eid = body.Eid,
                ResponseJson = body.ResponseJson,
                ResponseType = body.ResponseType,
                OwnerUserId = currentUser.Current.Id
            });
        } else {
            existing.ResponseJson = body.ResponseJson;
            existing.ResponseType = body.ResponseType;

            existing.UpdatedAt = time.GetUtcNow();
        }

        await db.SaveChangesAsync();
        return Ok(new { saved = body.Path, eid = body.Eid });
    }

    [HttpPost("route")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [RequiresDb]
    public async Task<IActionResult> AddRouteAsync([FromBody] AddRoute body, [FromServices] RouteCatalog yamlRoutes,
        [FromServices] EggIncognitoDbContext db, [FromServices] IDbRouteProvider? dbRoutes) {
        if (yamlRoutes.Resolve(body.Path) is not null ||
            await db.StoredRoutes.AsNoTracking().AnyAsync(r => r.Path == body.Path))
            return Fail(409, $"route {body.Path} already exists");

        db.StoredRoutes.Add(new StoredRoute {
            Path = body.Path,
            RequestType = body.RequestType,
            ResponseType = body.ResponseType,
            RequestWrapped = body.RequestWrapped ?? false,
            ResponseWrapped = body.ResponseWrapped ?? false,
            RawResponse = body.RawResponse,
            PathParam = body.PathParam ?? false,
            PathParamOnly = body.PathParamOnly ?? false,
            Source = "db",
            OwnerUserId = currentUser.Current.Id
        });
        try {
            await db.SaveChangesAsync();
        } catch (DbUpdateException) {
            return Fail(409, $"route {body.Path} already exists");
        }
        dbRoutes?.Invalidate();
        return Ok(new { added = body.Path });
    }

    [HttpGet("endpoints")]
    public async Task<IActionResult> ListEndpointsAsync([FromServices] EggIncognitoDbContext? db) {
        if (db is null) return Ok(Array.Empty<object>());
        var rows = await db.StoredEndpoints.AsNoTracking()
            .Select(e => new { e.Id, e.Path, e.ResponseType, e.UpdatedAt }).ToListAsync();
        return Ok(rows);
    }

    [HttpGet("routes")]
    public async Task<IActionResult> ListRoutesAsync([FromServices] EggIncognitoDbContext? db) {
        if (db is null) return Ok(Array.Empty<object>());
        var rows = await db.StoredRoutes.AsNoTracking().Where(r => r.Source == "db")
            .Select(r => new { r.Id, r.Path, r.RequestType, r.ResponseType }).ToListAsync();
        return Ok(rows);
    }
}
