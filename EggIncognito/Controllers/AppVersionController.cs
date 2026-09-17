using EggIncognito.Core.Services;
using EggIncognito.Models.Playground;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/app/version")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class AppVersionController : ControllerBase {
    private const string RepoUrl = "https://github.com/EggIncTools/EggIncognito";

    private static readonly AppVersionDto Payload = Build();

    private static AppVersionDto Build() {
        var b = BuildInfo.FromAssembly(RepoUrl);
        return new AppVersionDto(b.Version, b.ShortSha);
    }

    [HttpGet]
    public IActionResult Get() => Ok(Payload);
}
