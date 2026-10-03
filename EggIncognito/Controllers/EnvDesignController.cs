using System.Text;
using System.Text.Json;
using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIncognito.Data.Services;
using EggIncognito.Models.EnvDesign;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/env/designs")]
[ApiAccess(ApiAccessLevel.Contributor)]
public sealed class EnvDesignController(ICurrentUser currentUser) : ApiControllerBase {
    private const int MaxPayloadBytes = 2_000_000;

    [HttpGet]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> List([FromServices] EnvDesignStore? store, CancellationToken ct) {
        if (store is null) return Ok(new { designs = Array.Empty<object>() });
        var rows = await store.ListAsync(ct);
        return Ok(new { designs = rows });
    }

    [HttpGet("{name}")]
    [EnableRateLimiting("read")]
    [RequiresDb]
    public async Task<IActionResult> Get(string name, [FromServices] EnvDesignStore store, CancellationToken ct) {
        var row = await store.GetAsync(name, ct);
        return row is null ? Fail(404, "unknown design") : Content(row.Payload, "application/json");
    }

    [HttpPut("{name}")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Save(string name, [FromBody] SaveDesign body,
        [FromServices] EnvDesignStore store, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(name)) return Fail(400, "name required");

        string payload = body?.Payload ?? "";
        if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
            return Fail(400, "payload too large");
        try {
            using var _ = JsonDocument.Parse(payload);
        } catch {
            return Fail(400, "payload is not valid JSON");
        }

        int next = await store.SaveAsync(name, payload, currentUser.Current.Id, Trim(body?.Note), ct);
        return Ok(new { saved = name, version = next });
    }

    [HttpGet("{name}/versions")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Versions(string name, [FromServices] EnvDesignStore? store,
        CancellationToken ct) {
        if (store is null) return Ok(new { versions = Array.Empty<object>() });
        var (design, versions) = await store.VersionsAsync(name, ct);
        return design is null ? Fail(404, "unknown design") : Ok(new { versions });
    }

    [HttpPost("{name}/rollback")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Rollback(string name, [FromBody] RollbackBody body,
        [FromServices] EnvDesignStore store, CancellationToken ct) {
        var (result, fromVersion, next) = await store.RollbackAsync(name, body.VersionNo, currentUser.Current.Id, ct);
        return result switch {
            EnvDesignStore.RollbackResult.NoDesign => Fail(404, "unknown design"),
            EnvDesignStore.RollbackResult.NoVersion => Fail(404, "unknown version"),
            _ => Ok(new { rolledBack = name, fromVersion, newVersion = next })
        };
    }

    [HttpDelete("{name}")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Delete(string name, [FromServices] EnvDesignStore store, CancellationToken ct) {
        var result = await store.DeleteAsync(name, currentUser.Current.Id, currentUser.Current.IsAtLeast(UserRole.Admin), ct);
        return result switch {
            EnvDesignStore.DeleteResult.Ok => Ok(new { deleted = name }),
            EnvDesignStore.DeleteResult.Forbidden => Fail(403, "only the owner or an admin can delete this design"),
            _ => Fail(404, "unknown design")
        };
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
