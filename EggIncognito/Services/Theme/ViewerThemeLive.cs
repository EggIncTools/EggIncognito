using EggIdentity.Styles.Theming;

namespace EggIncognito.Services.Theme;

public sealed class ViewerThemeLive(ThemeCssEmitter emitter) {
    private const string Override = """:root:is(html, [data-eggidentity-theme="u"])""";

    public string? Css { get; private set; }

    public string? Nonce { get; set; }

    public event Action? Changed;

    public void Apply(ThemeModel model) {
        Css = emitter.Serialize(model with { Css = "" }, ThemeScope.Live, false)
            .Replace(ThemeCssSerializer.LivePrefix, Override, StringComparison.Ordinal);
        Changed?.Invoke();
    }
}
