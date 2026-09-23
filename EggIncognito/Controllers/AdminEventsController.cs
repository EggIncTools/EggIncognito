using System.Text.Json;
using EggIncognito.Data.Services;
using EggIncognito.Models.Events;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Events;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/admin/events")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("write")]
public sealed class AdminEventsController : ApiControllerBase {
    [HttpGet("stats")]
    [EnableRateLimiting("read")]
    [RequiresDb]
    public async Task<IActionResult> Stats([FromServices] EggIncognitoDbContext db, CancellationToken ct) {
        long total = await db.GameEvents.LongCountAsync(ct);
        long device = await db.GameEvents
            .LongCountAsync(e => e.Source == GameEventSources.Device, ct);
        var latest = await db.GameEvents.AsNoTracking()
            .OrderByDescending(e => e.StartTime)
            .FirstOrDefaultAsync(ct);
        return Ok(new EventStatsResponse(total, device, total - device,
            latest is null ? null : EventsController.ToDto(latest)));
    }

    [HttpPost("sweep-snapshots")]
    [RequiresDb]
    public async Task<IActionResult> SweepSnapshots([FromServices] GameEventBackfill backfill, CancellationToken ct) =>
        Ok(await backfill.SweepSnapshotsAsync(ct));

    [HttpPost("import-carpet")]
    [RequiresDb]
    public async Task<IActionResult> ImportCarpet(
        [FromBody] CarpetImportRequest request, [FromServices] GameEventBackfill backfill, CancellationToken ct) {
        if (!string.IsNullOrWhiteSpace(request?.Url) &&
            (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uri) ||
             uri.Scheme is not ("http" or "https")))
            return Fail(400, "invalid url");
        try {
            return Ok(await backfill.ImportCarpetAsync(request?.Url, ct));
        } catch (HttpRequestException ex) {
            return Fail(400, $"fetch failed: {ex.Message}");
        } catch (JsonException ex) {
            return Fail(400, $"invalid carpet payload: {ex.Message}");
        } catch (OperationCanceledException ex) {
            return Fail(400, $"fetch failed: {ex.Message}");
        }
    }
}
