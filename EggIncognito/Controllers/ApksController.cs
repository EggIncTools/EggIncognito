using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/apks")]
[ApiAccess(ApiAccessLevel.Admin)]
public sealed class ApksController : ApiControllerBase {
    [HttpGet]
    [EnableRateLimiting("read")]
    [RequiresDb]
    public async Task<IActionResult> List([FromServices] ApkStore store, CancellationToken ct) {
        var sets = await store.AllVersionsAsync(ct);
        var versions = sets.Select(Shape).ToList();
        return Ok(new StoredApkList(true, versions.Count, versions));
    }

    [HttpDelete]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Delete([FromQuery] string platform, [FromQuery] string package,
        [FromQuery] string appVersion, [FromQuery] string build, [FromServices] ApkStore store,
        CancellationToken ct) {
        int removed = await store.DeleteVersionAsync(platform, package, appVersion, build, ct);
        return removed > 0
            ? Ok(new { ok = true, platform, package, appVersion, build, removed })
            : Fail(404, $"no stored apk {package} {appVersion} ({build})");
    }

    private static StoredApkVersionRow Shape(ApkVersionSet set) => new(
        set.Platform, set.Package, set.AppVersion, set.Build, set.ByteSize, set.Installable, set.CapturedAt,
        [.. set.Splits.Select(s => new StoredApkSplitRow(s.Split, s.Sha256, s.ByteSize, s.SourceDeviceId,
            s.CapturedAt))]);
}
