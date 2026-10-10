using System.Buffers;
using EggIncognito.Core.Services.Assets;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/assets")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("asset")]
public sealed class AssetsController(GameAssetProvider assets) : ApiControllerBase {
    private static readonly SearchValues<char> InvalidNameChars = SearchValues.Create("/\\. ");

    [HttpGet("icon")]
    public async Task<IActionResult> Icon([FromQuery] string? name, [FromQuery] string? platform,
        CancellationToken ct) {
        if (string.IsNullOrEmpty(name) || name.AsSpan().IndexOfAny(InvalidNameChars) >= 0)
            return Fail(400, "invalid icon name");

        string? plat = string.IsNullOrEmpty(platform) ? null : platform;
        var result = await assets.GetAsync(new GameAssetKey("icon", plat, name), ct);
        if (!result.Ok || result.Asset is null)
            return Fail(404, result.Diagnostics ?? "icon not available", new { name });

        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue {
            Public = true,
            MaxAge = TimeSpan.FromDays(30)
        };
        return File(result.Asset.Bytes, result.Asset.ContentType);
    }
}
