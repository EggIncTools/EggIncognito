using EggIdentity.Auth;
using EggIncognito.Models.Coverage;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Coverage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/admin/consume-coverage")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("write")]
public sealed class ConsumeCoverageController(ICurrentUser currentUser) : ApiControllerBase {
    [HttpGet]
    [EnableRateLimiting("read")]
    [RequiresDb]
    public async Task<IActionResult> Get([FromServices] ConsumeCoverageService svc, CancellationToken ct) =>
        await svc.MapAsync(ct) is { } map ? Ok(map) : Fail(503, "artifact-catalog document missing");

    [HttpPut("targets")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    public async Task<IActionResult> Put([FromBody] CoverageTargetRequest body,
        [FromServices] ConsumeCoverageService svc, CancellationToken ct) {
        if (body.Error() is { } err) return Fail(400, err);
        var me = currentUser.Current;
        string by = me.Name ?? me.Id?.ToString() ?? "admin";
        return Ok(await svc.UpsertAsync(body, by, ct));
    }

    [HttpDelete("targets/{id:long}")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    public async Task<IActionResult> Delete(long id, [FromServices] ConsumeCoverageService svc,
        CancellationToken ct) =>
        await svc.DeleteAsync(id, ct) switch {
            null => Fail(404, "unknown target"),
            false => Fail(400, "the global target cannot be deleted"),
            true => NoContent()
        };
}
