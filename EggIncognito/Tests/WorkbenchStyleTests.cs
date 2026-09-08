using System.Text.RegularExpressions;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public partial class WorkbenchStyleTests(SharedAppFactory f) {
    [Fact]
    public async Task WbCard_DeclaresTheCardSizeTokens() {
        string css = await SheetAsync();

        Assert.Contains("width: var(--wb-card-w,92vw)", css);
        Assert.Contains("height: var(--wb-card-h,88vh)", css);
        Assert.Contains("max-width: var(--wb-card-max,80rem)", css);
    }

    [Fact]
    public async Task WideModifier_OverridesEveryCardToken() {
        string css = await SheetAsync();

        Assert.Contains("--wb-card-w: 94vw", css);
        Assert.Contains("--wb-card-h: 90vh", css);
        Assert.Contains("--wb-card-max: 92rem", css);
    }

    [Fact]
    public async Task RailWidth_IsATokenNotAFixedUtility() {
        string css = await SheetAsync();

        Assert.Contains("width: var(--wb-rail-w,18rem)", css);
    }

    [Fact]
    public async Task DeviceCard_DeclaresNoSizeOfItsOwn() {
        string css = await SheetAsync();

        foreach (Match m in DwbCardRegex().Matches(css)) {
            Assert.DoesNotContain("width", m.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("height", m.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ThemeCard_DeclaresNoSizeOfItsOwn() {
        string css = await SheetAsync();

        foreach (Match m in ThemeCardRegex().Matches(css)) {
            Assert.DoesNotContain("width", m.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("height", m.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ApiCard_DeclaresNoSizeOfItsOwn() {
        string css = await SheetAsync();

        Assert.Empty(AwbCardRegex().Matches(css));
    }

    [Fact]
    public async Task DeviceModeControl_IsGone() {
        string css = await SheetAsync();

        Assert.DoesNotContain(".dwb-mode", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThemeModeControl_IsGone() {
        string css = await SheetAsync();

        Assert.DoesNotContain(".theme-seg", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SharedVocabulary_SurvivesForItsRemainingConsumers() {
        string css = await SheetAsync();

        foreach (string cls in new[] {
                     ".wb-body", ".wb-rail", ".wb-rail-empty", ".wb-main", ".wb-head-tools",
                     ".wb-group", ".wb-group-head", ".wb-group-body",
                     ".wb-entry", ".wb-entry-name", ".wb-entry-meta", ".wb-entry-foot",
                     ".wb-x", ".wb-radio", ".wb-arrow",
                     ".wb-st-queued", ".wb-st-run", ".wb-st-done", ".wb-st-err", ".wb-st-offer"
                 }) {
            Assert.Contains(cls, css, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PlatformVocabulary_IsPresent() {
        string css = await SheetAsync();

        foreach (string cls in new[] {
                     ".wb-card", ".wb-card-wide", ".wb-notice", ".wb-rail-filter",
                     ".wb-sec", ".wb-sec-head", ".wb-sec-tools", ".wb-sec-body", ".wb-scroll",
                     ".wb-entry-head", ".wb-note", ".wb-st-muted",
                     ".modal-card-sm", ".modal-card-lg"
                 }) {
            Assert.Contains(cls, css, StringComparison.Ordinal);
        }
    }

    private async Task<string> SheetAsync() {
        var c = f.CreateClient();
        return await c.GetStringAsync("/styles.css");
    }

    [GeneratedRegex(@"^\s*\.dwb-card\s*\{[^}]*\}", RegexOptions.Multiline)]
    private static partial Regex DwbCardRegex();

    [GeneratedRegex(@"^\s*\.theme-wb-card\s*\{[^}]*\}", RegexOptions.Multiline)]
    private static partial Regex ThemeCardRegex();

    [GeneratedRegex(@"^\s*\.awb-card\s*\{[^}]*\}", RegexOptions.Multiline)]
    private static partial Regex AwbCardRegex();
}
