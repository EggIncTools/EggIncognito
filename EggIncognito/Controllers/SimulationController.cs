using EggIncognito.Core.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;

namespace EggIncognito.Controllers;

[ApiController]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class SimulationController(IBehaviorService behaviors) : ControllerBase {
    [HttpOptions("/")]
    public IActionResult GetAll() => Ok(behaviors.All());

    [HttpOptions("/{**slug}")]
    public IActionResult GetForSlug(string slug) => Ok(behaviors.ForEndpoint(slug));
}
