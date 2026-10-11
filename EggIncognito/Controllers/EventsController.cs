using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Events;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/v1/events")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class EventsController : ApiControllerBase {
    [HttpGet]
    [EnableRateLimiting("read")]
    [RequiresDb]
    public async Task<IActionResult> List(
        [FromServices] EggIncognitoDbContext db,
        [FromServices] TimeProvider time,
        [FromQuery] string? types,
        [FromQuery] bool? ultra,
        [FromQuery] double? after,
        [FromQuery] double? before,
        [FromQuery] double? activeAt,
        [FromQuery] bool active = false,
        [FromQuery] string? source = null,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken ct = default) {
        if (after is { } a && !UnixSeconds.IsValid(a)) return Fail(400, "after is out of range");
        if (before is { } b && !UnixSeconds.IsValid(b)) return Fail(400, "before is out of range");
        if (activeAt is { } at && !UnixSeconds.IsValid(at))
            return Fail(400, "activeAt is out of range");

        limit = Math.Clamp(limit, 1, 1000);
        offset = Math.Max(offset, 0);
        var q = db.GameEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(types)) {
            var wanted = types
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            q = q.Where(e => wanted.Contains(e.EventType));
        }
        if (ultra is { } u) q = q.Where(e => e.Ultra == u);
        if (after is { } a2) q = q.Where(e => e.StartTime >= UnixSeconds.ToTime(a2));
        if (before is { } b2) q = q.Where(e => e.StartTime <= UnixSeconds.ToTime(b2));
        double? instant = activeAt ?? (active ? UnixSeconds.FromTime(time.GetUtcNow()) : null);
        if (instant is { } inst) {
            var t = UnixSeconds.ToTime(inst);
            q = q.Where(e => e.StartTime <= t && e.EndTime >= t);
        }
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(e => e.Source == source);
        int total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(e => e.StartTime).ThenByDescending(e => e.Id)
            .Skip(offset).Take(limit).ToListAsync(ct);
        return Ok(new GameEventListResponse(total, rows.Select(ToDto).ToList()));
    }

    [HttpGet("predictions")]
    [EnableRateLimiting("read")]
    [Requires<EventPredictor>("no database configured")]
    public async Task<IActionResult> Predictions(
        [FromServices] EventPredictor predictor,
        [FromQuery] int horizon = 28, [FromQuery] double? asOf = null, CancellationToken ct = default) {
        if (asOf is { } at && !UnixSeconds.IsValid(at)) return Fail(400, "asOf is out of range");
        return Ok(await predictor.GetAsync(horizon, asOf, ct));
    }

    [HttpGet("model")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<EventPredictor>("no database configured")]
    public async Task<IActionResult> Model([FromServices] EventPredictor predictor, CancellationToken ct = default) =>
        Ok(await predictor.GetModelAsync(ct));

    [HttpGet("predictions/backtest")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<EventPredictor>("no database configured")]
    public async Task<IActionResult> PredictionsBacktest(
        [FromServices] EventPredictor predictor,
        [FromQuery] double? asOf, [FromQuery] int horizon = 28, CancellationToken ct = default) {
        if (asOf is not { } at) return Fail(400, "asOf is required");
        if (!UnixSeconds.IsValid(at)) return Fail(400, "asOf is out of range");
        return Ok(EventBacktest.Run(await predictor.RowsAsync(ct), at, horizon));
    }

    internal static GameEventDto ToDto(GameEvent e) => new(
        e.EventId, e.EventType, e.Message, e.Multiplier, e.Ultra,
        UnixSeconds.FromTime(e.StartTime), UnixSeconds.FromTime(e.EndTime), e.Source);
}
