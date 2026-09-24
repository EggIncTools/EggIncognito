using EggIncognito.Data.Services;
using EggIncognito.Models.Theme;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Theme;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/theme")]
[ApiAccess(ApiAccessLevel.Authenticated)]
public sealed class ThemeController(ICurrentUser currentUser, IConfiguration config) : ApiControllerBase {
    private const int MaxBodyBytes = 64 * 1024;

    private bool CustomCssConfigFloor() => config.GetValue("Theme:CustomCss", true);

    private static async Task AfterMutationAsync(IMemoryCache cache, ThemeIdentitySync sync, Guid uid,
        CancellationToken ct) {
        ThemeResolver.Invalidate(cache, uid);
        await sync.PushActiveAsync(uid, ct);
    }

    [HttpGet]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> List([FromServices] UserThemeStore? store) {
        if (currentUser.UserId is not { } uid) return Fail(401, "login required");
        if (store is null) return Ok(new { themes = Array.Empty<object>() });
        var rows = await store.ByOwnerAsync(uid, HttpContext.RequestAborted);
        return Ok(new {
            themes = rows.Select(t => new { t.Slug, t.Name, t.IsActive, t.SchemaVersion, t.UpdatedAt })
        });
    }

    [HttpGet("{slug}")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Get(string slug, [FromServices] UserThemeStore? store) {
        if (currentUser.UserId is not { } uid) return Fail(401, "login required");
        if (store is null) return Fail(404, "no database configured");
        var row = await store.GetAsync(uid, slug, HttpContext.RequestAborted);
        return row is null ? Fail(404, "unknown theme") : Content(row.Model, "application/json");
    }

    [HttpPut("{slug}")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Save(string slug, [FromServices] UserThemeStore store,
        [FromServices] IMemoryCache cache, [FromServices] ThemeIdentitySync sync) {
        if (currentUser.UserId is not { } uid) return Fail(401, "login required");
        string? json = await ReadBodyAsync();
        if (json is null) return Fail(400, "body too large");
        var (model, errors) = ThemeJson.Parse(json);
        if (model is null) return Fail(400, "invalid theme", new { details = errors });
        if (!string.Equals(model.Slug, slug, StringComparison.Ordinal))
            return Fail(400, "slug in the body must match the route");
        if (model.Css is { Length: > 0 })
            return Fail(400, "custom css is saved via PUT /api/theme/css");

        var existing = await store.GetAsync(uid, slug, HttpContext.RequestAborted);
        string keptCss = existing is not null ? ExtractCss(existing.Model) : "";
        var toStore = model with { Css = keptCss };
        var row = await store.UpsertAsync(uid, model.Slug, model.Name, model.SchemaVersion, toStore.ToJson(),
            HttpContext.RequestAborted);
        await AfterMutationAsync(cache, sync, uid, HttpContext.RequestAborted);
        return Ok(new { saved = row.Slug });
    }

    [HttpDelete("{slug}")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Delete(string slug, [FromServices] UserThemeStore store,
        [FromServices] IMemoryCache cache, [FromServices] ThemeIdentitySync sync) {
        if (currentUser.UserId is not { } uid) return Fail(401, "login required");
        bool deleted = await store.DeleteAsync(uid, slug, HttpContext.RequestAborted);
        if (!deleted) return Fail(404, "unknown theme");
        await AfterMutationAsync(cache, sync, uid, HttpContext.RequestAborted);
        return Ok(new { deleted = slug });
    }

    [HttpPost("{slug}/activate")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Activate(string slug, [FromServices] UserThemeStore store,
        [FromServices] IMemoryCache cache, [FromServices] ThemeIdentitySync sync) {
        if (currentUser.UserId is not { } uid) return Fail(401, "login required");
        var row = await store.GetAsync(uid, slug, HttpContext.RequestAborted);
        if (row is null) return Fail(404, "unknown theme");
        var (model, errors) = ThemeJson.Parse(row.Model);
        if (model is null)
            return Fail(422, "stored theme no longer parses", new { details = errors });
        var contrast = ThemePalette.Contrast(model);
        if (!contrast.Passes)
            return Fail(422, "contrast validation failed", new { failures = contrast.Failures });
        await store.ActivateAsync(uid, slug, System.Text.Json.JsonSerializer.Serialize(contrast),
            HttpContext.RequestAborted);
        await AfterMutationAsync(cache, sync, uid, HttpContext.RequestAborted);
        return Ok(new { activated = slug });
    }

    [HttpPost("deactivate")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Deactivate([FromServices] UserThemeStore store,
        [FromServices] IMemoryCache cache, [FromServices] ThemeIdentitySync sync) {
        if (currentUser.UserId is not { } uid) return Fail(401, "login required");
        await store.DeactivateAsync(uid, HttpContext.RequestAborted);
        await AfterMutationAsync(cache, sync, uid, HttpContext.RequestAborted);
        return Ok(new { deactivated = true });
    }

    [HttpPost("import")]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> Import([FromServices] UserThemeStore store,
        [FromServices] IMemoryCache cache, [FromServices] ThemeIdentitySync sync) {
        if (currentUser.UserId is not { } uid) return Fail(401, "login required");
        string? json = await ReadBodyAsync();
        if (json is null) return Fail(400, "body too large");
        var (model, errors) = ThemeJson.Parse(json);
        if (model is null) return Fail(400, "invalid theme", new { details = errors });
        if (model.Css is { Length: > 0 }) {
            var parsed = ThemeCss.Parse(model.Css);
            if (!parsed.Ok)
                return Fail(400, "invalid custom css", new { details = parsed.Errors });
        }

        var row = await store.UpsertAsync(uid, model.Slug, model.Name, model.SchemaVersion, model.ToJson(),
            HttpContext.RequestAborted);
        await AfterMutationAsync(cache, sync, uid, HttpContext.RequestAborted);
        return Ok(new { imported = row.Slug });
    }

    [HttpPut("css")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> SaveCss([FromBody] CssBody body, [FromServices] UserThemeStore store,
        [FromServices] IMemoryCache cache, [FromServices] ThemeIdentitySync sync) {
        if (currentUser.UserId is not { } uid) return Fail(401, "login required");
        if (!CustomCssConfigFloor()) return Fail(403, "custom css is disabled by configuration");
        var policy = await store.GetPolicyAsync(HttpContext.RequestAborted);
        if (!policy.CustomCssEnabled) return Fail(403, "custom css is disabled by the admin");

        string css = body.Css ?? "";
        if (css.Length > 0) {
            var parsed = ThemeCss.Parse(css);
            if (!parsed.Ok)
                return Fail(400, "invalid custom css", new { details = parsed.Errors });
        }

        var row = await store.GetAsync(uid, body.Slug ?? "", HttpContext.RequestAborted);
        if (row is null) return Fail(404, "unknown theme");
        var (model, errors) = ThemeJson.Parse(row.Model);
        if (model is null)
            return Fail(422, "stored theme no longer parses", new { details = errors });
        var updated = model with { Css = css };
        await store.UpsertAsync(uid, model.Slug, model.Name, model.SchemaVersion, updated.ToJson(),
            HttpContext.RequestAborted);
        await AfterMutationAsync(cache, sync, uid, HttpContext.RequestAborted);
        return Ok(new { saved = model.Slug });
    }

    [HttpGet("policy")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> GetPolicy([FromServices] UserThemeStore? store) {
        if (store is null)
            return Ok(new {
                customCssEnabled = true,
                configFloor = CustomCssConfigFloor(),
                defaultThemeSlug = (string?)null
            });

        var policy = await store.GetPolicyAsync(HttpContext.RequestAborted);
        string? defaultSlug = null;
        if (policy.DefaultThemeId is { } id)
            defaultSlug = (await store.GetByIdAsync(id, HttpContext.RequestAborted))?.Slug;
        return Ok(new {
            customCssEnabled = policy.CustomCssEnabled,
            configFloor = CustomCssConfigFloor(),
            defaultThemeSlug = defaultSlug
        });
    }

    [HttpPut("policy")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [EnableRateLimiting("write")]
    [RequiresDb]
    public async Task<IActionResult> SetPolicy([FromBody] PolicyBody body, [FromServices] UserThemeStore store) {
        if (currentUser.UserId is not { } uid) return Fail(401, "login required");

        long? defaultThemeId = null;
        if (!string.IsNullOrWhiteSpace(body.DefaultThemeSlug)) {
            var theme = await store.GetAsync(uid, body.DefaultThemeSlug, HttpContext.RequestAborted);
            if (theme is null)
                return Fail(400, "the default theme must be one of your own themes");
            if (ExtractCss(theme.Model).Length > 0)
                return Fail(400, "the default theme may not carry custom css");
            defaultThemeId = theme.Id;
        }

        await store.SetPolicyAsync(body.CustomCssEnabled, defaultThemeId, uid, HttpContext.RequestAborted);
        return Ok(new { saved = true });
    }

    private async Task<string?> ReadBodyAsync() {
        using var reader = new StreamReader(Request.Body);
        string body = await reader.ReadToEndAsync(HttpContext.RequestAborted);
        return System.Text.Encoding.UTF8.GetByteCount(body) > MaxBodyBytes ? null : body;
    }

    private static string ExtractCss(string modelJson) {
        var (model, _) = ThemeJson.Parse(modelJson);
        return model?.Css ?? "";
    }
}
