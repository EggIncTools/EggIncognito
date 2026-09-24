using EggIdentity.Client;
using EggIdentity.Styles.Theming;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Theme;

public sealed class ThemeIdentitySync(
    AuthState auth,
    IHttpContextAccessor httpContext,
    ILogger<ThemeIdentitySync> logger,
    IdentityApiClient? identity = null,
    UserThemeStore? store = null) {
    private const string NoTheme = "null";

    public async Task PushActiveAsync(Guid userId, CancellationToken ct) {
        if (Session() is not { } session) return;
        if (identity is null || store is null) return;

        var row = await store.ActiveForAsync(userId, ct);
        string payload = NoTheme;
        if (row is not null) {
            var (model, _) = ThemeJson.Parse(row.Model);
            if (model is not null) payload = (model with { Css = "" }).ToJson();
        }

        try {
            await identity.SetPreferencesAsync(session, null, null, payload, ct);
        } catch (HttpRequestException ex) {
            logger.LogWarning(ex, "theme not mirrored to the identity profile");
        } catch (TaskCanceledException ex) {
            logger.LogDebug(ex, "theme mirror cancelled");
        }
    }

    public async Task<ThemeModel?> FetchAsync(CancellationToken ct) {
        if (Session() is not { } session) return null;
        if (identity is null) return null;

        string? json;
        try {
            json = (await identity.GetProfileAsync(session, ct))?.Theme;
        } catch (HttpRequestException ex) {
            logger.LogWarning(ex, "shared theme not read from the identity profile");
            return null;
        } catch (TaskCanceledException ex) {
            logger.LogDebug(ex, "shared theme read cancelled");
            return null;
        }

        if (string.IsNullOrWhiteSpace(json) || json.Trim() == NoTheme) return null;
        var (model, errors) = ThemeJson.Parse(json);
        if (model is null) logger.LogWarning("shared theme rejected: {Errors}", string.Join("; ", errors));
        return model is null ? null : model with { Css = "" };
    }

    private string? Session() {
        string? token = httpContext.HttpContext?.Request.Cookies[auth.SessionCookieName];
        return string.IsNullOrEmpty(token) ? null : token;
    }
}
