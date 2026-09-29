using EggIncognito.Models.Coverage;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Coverage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/admin/consume-coverage")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("write")]
public sealed class ConsumeCoverageController(ICurrentUser currentUser) : ApiControllerBase {
    [HttpGet]
    [EnableRateLimiting("read")]
    [RequiresDb]
    public async Task<IActionResult> Get([FromServices] ConsumeCoverageService svc, CancellationToken ct) =>
        await svc.MapAsync(ct) is { } map ? Ok(map) : Fail(503, "artifact-catalog document missing");

    [HttpPut("targets")]
    [RequiresDb]
    public async Task<IActionResult> Put([FromBody] CoverageTargetRequest body,
        [FromServices] ConsumeCoverageService svc, CancellationToken ct) {
        if (body.Error() is { } err) return Fail(400, err);
        string by = currentUser.Username ?? currentUser.UserId?.ToString() ?? "admin";
        return Ok(await svc.UpsertAsync(body, by, ct));
    }

    [HttpDelete("targets/{id:long}")]
    [RequiresDb]
    public async Task<IActionResult> Delete(long id, [FromServices] ConsumeCoverageService svc,
        CancellationToken ct) =>
        await svc.DeleteAsync(id, ct) switch {
            null => Fail(404, "unknown target"),
            false => Fail(400, "the global target cannot be deleted"),
            true => NoContent()
        };
}
