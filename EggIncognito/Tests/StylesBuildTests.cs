using System.Net;
using System.Text.RegularExpressions;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public partial class StylesBuildTests(SharedAppFactory f) {
    [Theory]
    [InlineData(SheetFetch.SharedPath)]
    [InlineData(SheetFetch.AppPath)]
    public async Task GlobalSheet_IsServed_AndNonEmpty(string path) {
        var c = f.CreateClient();
        var r = await c.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string css = await r.Content.ReadAsStringAsync();
        Assert.True(css.Length > 500, $"{path} looks empty");
    }

    [Fact]
    public async Task AppSheet_IsPlainCss() {
        string css = await f.CreateClient().GetStringAsync(SheetFetch.AppPath);
        foreach (string directive in new[] { "@theme", "@apply", "@layer", "@utility", "@source", "@custom-variant" }) {
            Assert.DoesNotContain(directive, css);
        }
    }

    [Fact]
    public async Task EveryReferencedColorToken_IsDefinedInTheGlobalSheets() {
        string css = await SheetFetch.AllAsync(f.CreateClient());

        var defined = DefinitionRegex().Matches(css).Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var missing = ReferenceRegex().Matches(css).Select(m => m.Groups[1].Value)
            .Where(name => !defined.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "color tokens are referenced by var() but never defined, so every declaration using them is invalid "
            + "at computed-value time and silently falls back to its initial value: "
            + string.Join(", ", missing.Select(n => "--color-" + n)));
    }

    [GeneratedRegex(@"--color-([a-z0-9-]+)\s*:")]
    private static partial Regex DefinitionRegex();

    [GeneratedRegex(@"var\(\s*--color-([a-z0-9-]+)\s*[,)]")]
    private static partial Regex ReferenceRegex();
}
