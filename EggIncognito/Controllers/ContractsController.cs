using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Contracts;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/v1/contracts")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class ContractsController : ApiControllerBase {
    [HttpGet]
    [EnableRateLimiting("read")]
    [RequiresDb]
    public async Task<IActionResult> List(
        [FromServices] EggIncognitoDbContext db,
        [FromQuery] double? after,
        [FromQuery] double? before,
        [FromQuery] bool? leggacy,
        [FromQuery] bool? ultra,
        [FromQuery] string? search,
        [FromQuery] int limit = 500,
        CancellationToken ct = default) {
        if (after is { } a && !UnixSeconds.IsValid(a)) return Fail(400, "after is out of range");
        if (before is { } b && !UnixSeconds.IsValid(b)) return Fail(400, "before is out of range");

        limit = Math.Clamp(limit, 1, 1000);
        var q = db.ContractReleases.AsNoTracking();
        if (after is { } a2) q = q.Where(r => r.EndTime >= UnixSeconds.ToTime(a2));
        if (before is { } b2) q = q.Where(r => r.StartTime <= UnixSeconds.ToTime(b2));
        if (leggacy is { } l) q = q.Where(r => r.Leggacy == l);
        if (ultra is { } u) q = q.Where(r => r.UltraOnly == u);
        if (!string.IsNullOrWhiteSpace(search)) {
            var pattern = $"%{search}%";
            q = q.Where(r => EF.Functions.ILike(r.ContractId, pattern) || EF.Functions.ILike(r.Name, pattern));
        }
        int total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.StartTime).ThenByDescending(r => r.Id).Take(limit).ToListAsync(ct);
        return Ok(new ContractReleaseListResponse(total, rows.Select(ToDto).ToList()));
    }

    [HttpGet("predictions")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<ContractPredictor>("no database configured")]
    public async Task<IActionResult> Predictions(
        [FromServices] ContractPredictor predictor,
        [FromQuery] string? contract,
        [FromQuery] int horizon = 9,
        CancellationToken ct = default) {
        if (!string.IsNullOrWhiteSpace(contract)) {
            var estimate = await predictor.GetContractAsync(contract.Trim(), ct);
            if (estimate is null) return Fail(404, "unknown contract");
            return Ok(estimate);
        }
        return Ok(await predictor.GetSlotsAsync(Math.Clamp(horizon, 1, 30), ct));
    }

    [HttpGet("predictions/backtest")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<ContractPredictor>("no database configured")]
    public async Task<IActionResult> PredictionsBacktest(
        [FromServices] ContractPredictor predictor,
        [FromQuery] double? asOf, [FromQuery] int horizon = 9, CancellationToken ct = default) {
        if (asOf is not { } at) return Fail(400, "asOf is required");
        if (!UnixSeconds.IsValid(at)) return Fail(400, "asOf is out of range");
        return Ok(ContractBacktest.Run(await predictor.SamplesAsync(ct), at, horizon));
    }

    [HttpGet("model")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [Requires<ContractPredictor>("no database configured")]
    public async Task<IActionResult> Model([FromServices] ContractPredictor predictor, CancellationToken ct = default) =>
        Ok(await predictor.GetModelAsync(ct));

    internal static ContractReleaseDto ToDto(ContractRelease r) => new(
        r.Id, r.ContractId, r.Name, r.Egg, r.CustomEggId, r.SeasonId,
        UnixSeconds.FromTime(r.StartTime), UnixSeconds.FromTime(r.EndTime), r.LengthSeconds,
        r.Leggacy, r.UltraOnly, r.ProphecyEggs, r.CoopAllowed, r.MaxCoopSize, r.Source);
}
