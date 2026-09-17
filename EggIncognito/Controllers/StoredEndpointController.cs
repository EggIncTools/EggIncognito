using EggIdentity.Contract;
using EggIncognito.Core.Services;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Endpoints;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/db")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("write")]
public sealed class StoredEndpointController(ICurrentUser currentUser, IServiceProvider services) : ControllerBase {
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    private IDbRouteProvider? DbRoutes => services.GetService(typeof(IDbRouteProvider)) as IDbRouteProvider;

    private ObjectResult? RequireContributor() =>
        currentUser.IsAtLeast(UserRole.Contributor)
            ? null
            : StatusCode(403, new { error = "contributor role required to write to the shared store" });

    [HttpPost("endpoint")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    public async Task<IActionResult> UpsertEndpointAsync([FromBody] UpsertEndpoint body,
        [FromServices] IRouteCatalog routes) {
        if (RequireContributor() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        if (routes.Resolve(body.Path) is null) return BadRequest(new { error = $"unknown route {body.Path}" });

        var existing = await db.StoredEndpoints
            .FirstOrDefaultAsync(e => e.Path == body.Path && e.Eid == body.Eid);
        if (existing is null) {
            db.StoredEndpoints.Add(new StoredEndpoint {
                Path = body.Path,
                Eid = body.Eid,
                ResponseJson = body.ResponseJson,
                ResponseType = body.ResponseType,
                OwnerUserId = currentUser.UserId
            });
        } else {
            existing.ResponseJson = body.ResponseJson;
            existing.ResponseType = body.ResponseType;

            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
        return Ok(new { saved = body.Path, eid = body.Eid });
    }

    [HttpPost("route")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    public async Task<IActionResult> AddRouteAsync([FromBody] AddRoute body, [FromServices] RouteCatalog yamlRoutes) {
        if (RequireContributor() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        if (yamlRoutes.Resolve(body.Path) is not null ||
            await db.StoredRoutes.AsNoTracking().AnyAsync(r => r.Path == body.Path))
            return Conflict(new { error = $"route {body.Path} already exists" });

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
            OwnerUserId = currentUser.UserId
        });
        try {
            await db.SaveChangesAsync();
        } catch (DbUpdateException) {
            return Conflict(new { error = $"route {body.Path} already exists" });
        }
        DbRoutes?.Invalidate();
        return Ok(new { added = body.Path });
    }

    [HttpGet("endpoints")]
    public async Task<IActionResult> ListEndpointsAsync() {
        var db = Db;
        if (db is null) return Ok(Array.Empty<object>());
        var rows = await db.StoredEndpoints.AsNoTracking()
            .Select(e => new { e.Id, e.Path, e.ResponseType, e.UpdatedAt }).ToListAsync();
        return Ok(rows);
    }

    [HttpGet("routes")]
    public async Task<IActionResult> ListRoutesAsync() {
        var db = Db;
        if (db is null) return Ok(Array.Empty<object>());
        var rows = await db.StoredRoutes.AsNoTracking().Where(r => r.Source == "db")
            .Select(r => new { r.Id, r.Path, r.RequestType, r.ResponseType }).ToListAsync();
        return Ok(rows);
    }
}
