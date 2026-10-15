using System.Globalization;
using EggIdentity.Auth;
using EggIdentity.Settings.Store;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Config;
using EggIncognito.Services.Predictions.Training;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/v1/predictions")]
[ApiAccess(ApiAccessLevel.Admin)]
public sealed class PredictionsController : ApiControllerBase {
    [HttpGet("training")]
    [EnableRateLimiting("read")]
    [Requires<PredictionTrainer>("no database configured")]
    public IActionResult Training([FromServices] PredictionTrainer trainer) => Ok(trainer.Statuses);

    [HttpPost("training/{domain}/run")]
    [EnableRateLimiting("write")]
    [Requires<PredictionTrainer>("no database configured")]
    public IActionResult Run(
        [FromServices] PredictionTrainer trainer, PredictionDomainKey domain, [FromQuery] bool force = false) {
        trainer.Request(domain, force);
        return Ok(trainer.Status(domain));
    }

    public sealed record HorizonsRequest(int EventDays, int ContractWeeks, int StepDays, int RefreshHours);

    [HttpPut("horizons")]
    [EnableRateLimiting("write")]
    [Requires<PredictionTrainer>("no database configured")]
    public async Task<IActionResult> Horizons(
        [FromServices] PredictionTrainer trainer, [FromServices] SettingsAdminService settings,
        [FromServices] ICurrentUser user, [FromBody] HorizonsRequest body, CancellationToken ct) {
        (string Key, int Value)[] pairs = [
            (SettingKeys.PredictionsEventsMaxHorizonDays, Math.Clamp(body.EventDays, 30, 365)),
            (SettingKeys.PredictionsContractsMaxHorizonWeeks, Math.Clamp(body.ContractWeeks, 4, 104)),
            (SettingKeys.PredictionsHorizonStepDays, Math.Max(1, body.StepDays)),
            (SettingKeys.PredictionsRefreshHours, Math.Max(1, body.RefreshHours))
        ];
        string by = user.Current.Name ?? user.Current.DiscordId ?? "admin";
        foreach (var (key, value) in pairs) {
            var saved = await settings.SaveAsync(key, value.ToString(CultureInfo.InvariantCulture), by, ct);
            if (!saved.Ok) return Fail(400, saved.Error ?? $"could not save {key}");
        }

        trainer.Request(PredictionDomainKey.Events, force: true);
        trainer.Request(PredictionDomainKey.Contracts, force: true);
        return Ok(trainer.Statuses);
    }

    [HttpPost("training/{domain}/cancel")]
    [EnableRateLimiting("write")]
    [Requires<PredictionTrainer>("no database configured")]
    public IActionResult Cancel([FromServices] PredictionTrainer trainer, PredictionDomainKey domain) {
        trainer.Cancel(domain);
        return Ok(trainer.Status(domain));
    }
}
