using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public partial class UnifiedStyleTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _f = f;

    [Theory]
    [InlineData("/protos")]
    [InlineData("/capture")]
    public async Task Page_LinksSharedThenAppThenScoped(string path) {
        var c = _f.CreateClient();
        string html = await c.GetStringAsync(path);
        int shared = html.IndexOf(SheetFetch.SharedPath, StringComparison.Ordinal);
        int app = html.IndexOf("\"" + SheetFetch.AppPath, StringComparison.Ordinal);
        int scoped = html.IndexOf("/EggIncognito.styles.css", StringComparison.Ordinal);
        Assert.True(shared >= 0 && app > shared && scoped > app, "expected shared.css, then app.css, then the scoped bundle");
        Assert.DoesNotContain("/styles.css", html.Replace("/EggIncognito.styles.css", "", StringComparison.Ordinal));
        Assert.DoesNotContain("/tailwind.css", html);
    }

    [Fact]
    public async Task CompiledSheet_DefinesUnifiedComponentVocabulary() {
        var c = _f.CreateClient();
        string css = await SheetFetch.AllAsync(c);
        foreach (string cls in new[] {
                     ".panel", ".btn-primary", ".icon-btn", ".settings-menu", ".result-pre",
                     ".flow-row", ".jtree-root", ".stage-row", ".flow-filters",
                     ".verline-app", ".verline-num", ".verline-sep", ".platform-icon", ".route-flag",
                     ".toast", ".modal-card", ".known-card", ".detail-pane-title", ".notif-item",
                     ".perk-list", ".rail", ".connect-card", ".faq-list",
                     ".data-table", ".stat-tile",
                     ".reg-filter-input",
                     ".prose-legal", ".prose-legal-section",
                     ".popover", ".popover-combo", ".popover-combo-opt",
                     ".tt-pop", ".tt-title", ".tt-line",
                     ".pg-menubar", ".pg-menu-btn", ".pg-menu", ".pg-menu-item", ".pg-popover",
                     ".pg-popover-head"
                 }) {
            Assert.Contains(cls, css);
        }
    }

    [Fact]
    public async Task CompiledSheet_KeepsDynamicallyReferencedClasses() {
        var c = _f.CreateClient();
        string css = await SheetFetch.AllAsync(c);
        foreach (string cls in new[] {
                     ".tok-string", ".tok-number", ".tok-bool", ".tok-null",
                     ".toast-info", ".theme-color-input", ".picker"
                 }) {
            Assert.Contains(cls, css);
        }
    }

    [Fact]
    public async Task BtnPrimary_IsAccentOrange_NotAccent2Blue() {
        var c = _f.CreateClient();
        string css = await c.GetStringAsync(SheetFetch.AppPath);

        Assert.Contains("#ef7559", css);

        var m = BtnPrimaryRegex().Match(css);
        Assert.True(m.Success, ".btn-primary rule not found");
        Assert.Contains("var(--color-accent)", m.Value);
        Assert.DoesNotContain("accent2", m.Value);
        Assert.DoesNotContain("#5aa9e6", m.Value);
    }

    [GeneratedRegex(@"^\s*\.btn-primary\s*\{[^}]*\}", RegexOptions.Multiline)]
    private static partial Regex BtnPrimaryRegex();
}
