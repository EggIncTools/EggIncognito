using EggIncognito.Core.Services.Assets;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/assets")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("read")]
public sealed class AssetsController(GameAssetProvider assets) : ApiControllerBase {
    [HttpGet("icon")]
    public async Task<IActionResult> Icon([FromQuery] string? name, [FromQuery] string? platform,
        CancellationToken ct) {
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(['/', '\\', '.', ' ']) >= 0)
            return Fail(400, "invalid icon name");

        string? plat = string.IsNullOrEmpty(platform) ? null : platform;
        var result = await assets.GetAsync(new GameAssetKey("icon", plat, name), ct);
        if (!result.Ok || result.Asset is null)
            return StatusCode(404, new ApiError(result.Diagnostics ?? "icon not available", null, 404, new { name }));

        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue {
            Public = true,
            MaxAge = TimeSpan.FromDays(30)
        };
        return File(result.Asset.Bytes, result.Asset.ContentType);
    }
}
