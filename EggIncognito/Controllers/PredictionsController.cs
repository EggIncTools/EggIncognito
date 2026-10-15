using EggIncognito.Services.Auth;
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

    [HttpPost("training/{domain}/cancel")]
    [EnableRateLimiting("write")]
    [Requires<PredictionTrainer>("no database configured")]
    public IActionResult Cancel([FromServices] PredictionTrainer trainer, PredictionDomainKey domain) {
        trainer.Cancel(domain);
        return Ok(trainer.Status(domain));
    }
}
