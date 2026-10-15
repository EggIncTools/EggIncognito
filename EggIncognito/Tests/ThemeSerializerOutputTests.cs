using EggIdentity.Styles.Theming;
using EggIncognito.Services.Theme;

namespace EggIncognito.Tests;

public class ThemeSerializerOutputTests {
    private static readonly string[] AdversarialCss = [
        "panel { color: red; background-color: #112233 }",
        "button { opacity: 0.5; transition-duration: 200ms; box-shadow: 0 0 8px color-mix(in oklab, var(--color-accent) 40%, transparent) }",
        "table-header { font-weight: 700; letter-spacing: 0.1em; text-transform: uppercase }",
        "panel { background-image: linear-gradient(to top right, var(--color-panel) 0%, var(--color-accent) 100%) }",
        "scrollbar-thumb { color: oklch(70% 0.1 200); background-color: hsl(200, 40%, 20%) }",
        "modal-card { border-radius: 12px 4px; border-width: 2px; border-style: dashed; border-color: rgba(10, 20, 30, 0.5) }"
    ];

    [Fact]
    public void AdversarialModels_NeverEmitForbiddenBytes() {
        var serializer = ThemeTestSupport.Serializer();
        foreach (string source in AdversarialCss) {
            string css = serializer.Serialize(ThemePresets.Default.WithCss(source), ThemeScope.Live, true);
            AssertAlphabet(css, source);
            Assert.NotEqual("", css);
        }
    }

    [Fact]
    public void LiveScope_PrefixesTheHtmlAttributeSelector() {
        string css = ThemeTestSupport.Serializer()
            .Serialize(ThemePresets.Default.WithCss("panel { color: red }"), ThemeScope.Live, true);
        Assert.Contains("html[data-eggidentity-theme=\"u\"] .panel", css);
        Assert.DoesNotContain(".theme-preview-scope", css);
    }

    [Fact]
    public void PreviewScope_PrefixesThePreviewClass() {
        string css = ThemeTestSupport.Serializer()
            .Serialize(ThemePresets.Default.WithCss("panel { color: red }"), ThemeScope.Preview, true);
        Assert.Contains(".theme-preview-scope .panel", css);
        Assert.DoesNotContain("html[data-eggidentity-theme", css);
    }

    [Fact]
    public void CustomCssDisallowed_EmitsNoLane2() {
        string css = ThemeTestSupport.Serializer()
            .Serialize(ThemePresets.Default.WithCss("panel { color: red }"), ThemeScope.Live, false);
        Assert.DoesNotContain(".panel", css);
    }

    [Fact]
    public void MultiSelectorCatalogEntry_ScopesEveryPart() {
        string css = ThemeTestSupport.Serializer()
            .Serialize(ThemePresets.Default.WithCss("input { color: red }"), ThemeScope.Live, true);
        Assert.Contains("html[data-eggidentity-theme=\"u\"] .reg-filter-input", css);
        Assert.Contains("html[data-eggidentity-theme=\"u\"] .protos-filter", css);
    }

    [Fact]
    public void StagingDropsAccent_AndLane2() {
        var serializer = ThemeTestSupport.Serializer("Staging");
        var model = ThemePresets.Default.WithCss("panel { color: red }");
        string css = serializer.Serialize(model, ThemeScope.Live, true);
        Assert.DoesNotContain("--color-accent", css);
        Assert.DoesNotContain("--egi-glow", css);
        Assert.DoesNotContain("--egi-panel-tint", css);
        Assert.DoesNotContain("--egi-accent-grad-to", css);
        Assert.DoesNotContain(".panel", css);
        Assert.Contains("--color-bg: #1b1b1f;", css);
    }

    [Fact]
    public void StagingDropsHueRotation() {
        string css = ThemeTestSupport.Serializer("Staging").Serialize(ThemePresets.Prism, ThemeScope.Live, true);
        Assert.DoesNotContain("egi-hue", css);
        Assert.DoesNotContain("animation", css);
    }

    [Fact]
    public void HueRotation_EmitsKeyframesAndReducedMotionGuard() {
        string css = ThemeTestSupport.Serializer().Serialize(ThemePresets.Prism, ThemeScope.Live, true);
        Assert.Contains("@keyframes egi-hue", css);
        Assert.Contains("prefers-reduced-motion", css);
        Assert.Contains("calc(", css);
        Assert.Contains("var(--egi-hue-shift)", css);
    }

    [Fact]
    public void GlowValue_EndsWithTheSpliceComma() {
        string css = ThemeTestSupport.Serializer().Serialize(ThemePresets.Violet, ThemeScope.Live, true);
        Assert.Contains("transparent),;", css);
    }

    private static void AssertAlphabet(string css, string context) {
        Assert.DoesNotContain("<", css);
        Assert.DoesNotContain("\\", css);
        Assert.DoesNotContain("&", css);
        int at = 0;
        while ((at = css.IndexOf('@', at)) >= 0) {
            bool ok = css[at..].StartsWith("@keyframes", StringComparison.Ordinal)
                      || css[at..].StartsWith("@media", StringComparison.Ordinal);
            Assert.True(ok, $"unexpected '@' in output for {context}");
            at++;
        }
    }
}
