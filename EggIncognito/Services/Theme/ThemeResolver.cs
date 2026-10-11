using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIdentity.Styles.Theming;
using EggIdentity.UI.Suite;
using EggIncognito.Data.Services;
using Microsoft.Extensions.Caching.Memory;

namespace EggIncognito.Services.Theme;

public sealed record ResolvedTheme(string Css, bool HueRotation);

public sealed class ThemeResolver(
    ICurrentUser currentUser,
    SuiteContext suite,
    IMemoryCache cache,
    ThemeCssEmitter emitter,
    IConfiguration configuration,
    IHostEnvironment environment,
    UserThemeStore? themeStore = null) {
    public const string NoTheme = "null";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private sealed record Cached(string Source, ResolvedTheme? Resolved);

    public static ThemeModel ViewerModel(ThemeModel model, IHostEnvironment environment) {
        if (!environment.IsStaging()) return model with { Css = "" };
        var tokens = new Dictionary<string, ThemeTokenValue>(model.Tokens, StringComparer.Ordinal);
        tokens.Remove("accent");
        return model with { Css = "", Tokens = tokens };
    }

    public static string CacheKey(Guid userId) => $"egi.theme.{userId:N}";

    public static void Invalidate(IMemoryCache cache, Guid userId) => cache.Remove(CacheKey(userId));

    public static ThemeModel? Shared(string? value) {
        if (string.IsNullOrWhiteSpace(value) || value.Trim() == NoTheme) return null;
        var (model, _) = ThemeJson.Parse(value);
        return model is null || !ThemePalette.Contrast(model).Passes ? null : model with { Css = "" };
    }

    public async Task<ResolvedTheme?> ResolveAsync(CancellationToken ct = default) {
        await suite.LoadAsync(ct);
        string shared = suite.Theme;
        if (currentUser.Current is not { IsAuthenticated: true, Id: { } uid } || themeStore is not { } store)
            return Shared(shared) is { } viewer ? Emit(ViewerModel(viewer, environment), false) : null;

        if (cache.TryGetValue(CacheKey(uid), out Cached? hit) && hit is not null && hit.Source == shared) return hit.Resolved;
        var resolved = await ResolveSignedInAsync(store, uid, shared, ct);
        cache.Set(CacheKey(uid), new Cached(shared, resolved), CacheTtl);
        return resolved;
    }

    private async Task<ResolvedTheme?> ResolveSignedInAsync(UserThemeStore store, Guid uid, string shared, CancellationToken ct) {
        if (Shared(shared) is { } model) {
            string css = await CustomCssAsync(store, uid, model.Slug, ct);
            return Emit(model with { Css = css }, css.Length > 0);
        }

        var policy = await store.GetPolicyAsync(ct);
        if (policy.DefaultThemeId is not { } defaultId || await store.GetByIdAsync(defaultId, ct) is not { } row) return null;
        var (fallback, _) = ThemeJson.Parse(row.Model);
        return fallback is null ? null : Emit(fallback with { Css = "" }, false);
    }

    private async Task<string> CustomCssAsync(UserThemeStore store, Guid uid, string slug, CancellationToken ct) {
        if (!configuration.GetValue("Theme:CustomCss", true)) return "";
        if (!currentUser.Current.IsAtLeast(UserRole.Contributor)) return "";
        if (!(await store.GetPolicyAsync(ct)).CustomCssEnabled) return "";
        if (await store.GetAsync(uid, slug, ct) is not { } row) return "";
        return ThemeJson.Parse(row.Model).Model?.Css ?? "";
    }

    private ResolvedTheme? Emit(ThemeModel model, bool customCss) {
        string css = emitter.Serialize(model, ThemeScope.Live, customCss);
        return css.Length == 0 ? null : new ResolvedTheme(css, ThemeCssEmitter.UsesHueRotation(model));
    }
}
