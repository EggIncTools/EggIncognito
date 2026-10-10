using EggIdentity.Styles.Theming;

namespace EggIncognito.Services.Theme;

public sealed class ViewerThemeLive(ThemeCssEmitter emitter, ThemeResolver resolver, IHostEnvironment environment) {
    private const string Override = """:root:is(html, [data-eggidentity-theme="u"])""";

    public string? Css { get; private set; }

    public string? Nonce { get; set; }

    public event Action? Changed;

    public void Apply(ThemeModel model) {
        Set(emitter.Serialize(ThemeResolver.ViewerModel(model, environment), ThemeScope.Live, false));
    }

    public async Task ClearAsync() {
        var server = await resolver.ResolveAsync();
        Set(server?.Css ?? emitter.Serialize(ThemeResolver.ViewerModel(ThemePresets.Default, environment), ThemeScope.Live, false));
    }

    private void Set(string css) {
        Css = css.Replace(ThemeCssSerializer.LivePrefix, Override, StringComparison.Ordinal);
        Changed?.Invoke();
    }
}
