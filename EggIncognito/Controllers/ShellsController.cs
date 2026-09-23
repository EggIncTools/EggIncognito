using EggIncognito.Core.Services.Assets;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Ei;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/shells")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class ShellsController(
    GameConfigStore configStore,
    ShipShellDownloader downloader,
    MeshAssetCache cache,
    IAppMode appMode,
    ICurrentUser currentUser) : ApiControllerBase {
    [HttpGet]
    public IActionResult List([FromQuery] string platform = "ios", [FromQuery] string? assetType = null) {
        var catalog = LoadCatalog(platform);
        if (catalog is null)
            return Ok(new { ok = false, diagnostics = $"no stored config for {platform}; ingest one via /api/config" });

        var shells = string.IsNullOrEmpty(assetType)
            ? ShellCatalog.FromCatalog(catalog)
            : ShellCatalog.ForAssetType(catalog, assetType);

        string[] assetTypes = [
            .. ShellCatalog.FromCatalog(catalog)
                .Select(s => s.AssetType).Distinct(StringComparer.Ordinal).OrderBy(a => a, StringComparer.Ordinal)
        ];

        return Ok(new {
            ok = true,
            platform,
            assetTypes,
            count = shells.Count,
            shells = shells.Select(s => new { s.Identifier, s.Name, s.AssetType, s.Url, s.ModifiedGeometry })
        });
    }

    [HttpGet("objects")]
    public IActionResult Objects([FromQuery] string platform = "ios", [FromQuery] string? type = null) {
        var catalog = LoadCatalog(platform);
        if (catalog is null)
            return Ok(new {
                ok = false,
                platform,
                type = type ?? "",
                diagnostics = $"no stored config for {platform}; ingest one via /api/config"
            });

        var objs = type?.ToLowerInvariant() switch {
            "chicken" => ShellCatalog.Chickens(catalog),
            "hat" => ShellCatalog.Hats(catalog),
            _ => ShellCatalog.Objects(catalog)
        };

        return Ok(new {
            ok = true,
            platform,
            type = type ?? "",
            count = objs.Count,
            objects = objs.Select(o => new { o.Identifier, o.Name, o.AssetType, o.Url, anchor = o.Anchor, o.NoHats })
        });
    }

    [HttpGet("sets")]
    public IActionResult Sets([FromQuery] string platform = "ios") {
        var catalog = LoadCatalog(platform);
        if (catalog is null)
            return Ok(new { ok = false, platform, diagnostics = $"no stored config for {platform}; ingest one via /api/config" });

        static object Shape(ShellCatalog.ShellSet s) {
            return new {
                s.Identifier,
                s.Name,
                s.Decorator,
                members = s.Members.Select(m => new { m.Identifier, m.AssetType })
            };
        }

        return Ok(new {
            ok = true,
            platform,
            sets = ShellCatalog.Sets(catalog).Select(Shape),
            decorators = ShellCatalog.Decorators(catalog).Select(Shape)
        });
    }

    [HttpGet("{platform}/{identifier}/glb")]
    [EnableRateLimiting("egress")]
    public async Task<IActionResult> Glb(string platform, string identifier, [FromQuery] string? animate,
        [FromQuery] float seconds, CancellationToken ct) {
        if (appMode.Mode == AppMode.Hosted && !currentUser.IsAuthenticated)
            return Fail(403, "log in to download shell meshes from the hosted site");

        string cacheKey = $"{platform}_{identifier}";
        byte[]? glb = cache.TryGet("shell", cacheKey);
        if (glb is null) {
            var catalog = LoadCatalog(platform);
            if (catalog is null) return Fail(404, $"no stored config for {platform}");
            string? url = ShellCatalog.ById(catalog, identifier)?.Url
                          ?? ShellCatalog.ObjectById(catalog, identifier)?.Url;
            if (url is null) return Fail(404, "unknown shell identifier");

            var decode = await downloader.DownloadAndDecodeAsync(url, identifier, ct);
            if (!decode.Ok) return Ok(new { ok = false, diagnostics = decode.Diagnostics });
            glb = decode.Glb!;
            await cache.PutAsync("shell", cacheKey, glb, ct);
        }

        if (!string.IsNullOrEmpty(animate)) {
            var opts = new GltfAnimator.Options(
                GltfAnimator.ParseKind(animate), seconds > 0 ? seconds : 6f);
            var anim = GltfAnimator.Animate(glb, opts);
            if (anim.Ok) glb = anim.Glb!;
        }

        return File(glb, "model/gltf-binary", $"{identifier}.glb");
    }

    private DLCCatalog? LoadCatalog(string platform) {
        var stored = configStore.Get(platform);
        if (stored is null) return null;
        try {
            var cfg = ConfigResponse.Parser.ParseJson(stored.Json);
            return cfg.DlcCatalog;
        } catch {
            return null;
        }
    }
}
