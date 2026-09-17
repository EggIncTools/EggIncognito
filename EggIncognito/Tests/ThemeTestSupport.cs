using EggIdentity.Styles.Theming;
using EggIncognito.Services.Theme;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public static class ThemeTestSupport {
    public static ThemeCssEmitter Serializer(string environment = "Production") =>
        new(new FakeWebHostEnvironment(environment), NullLogger<ThemeCssEmitter>.Instance);

    public static ThemeModel WithCss(this ThemeModel model, string css) => model with { Css = css };
}
