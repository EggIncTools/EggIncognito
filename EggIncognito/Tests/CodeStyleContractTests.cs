using System.Globalization;
using System.Text.RegularExpressions;
using EggIncognito.Components.Shared.Code;
using EggIncognito.Core.Services.Syntax;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public partial class CodeStyleContractTests(SharedAppFactory f) {
    [Fact]
    public async Task CompiledSheet_ShipsEveryTokenClass() {
        string css = await SheetFetch.AllAsync(f.CreateClient());
        foreach (string cls in TokenClasses.All) {
            Assert.Contains("." + cls, css, StringComparison.Ordinal);
        }

        Assert.Equal(19, TokenClasses.All.Count);
    }

    [Fact]
    public async Task TokenClassRules_CarryNoHexLiteral() {
        string css = await SheetFetch.AllAsync(f.CreateClient());
        foreach (string cls in TokenClasses.All) {
            var m = Regex.Match(css, @"^\s*\." + Regex.Escape(cls) + @"\s*\{[^}]*\}", RegexOptions.Multiline);
            Assert.True(m.Success, "rule not found for ." + cls);
            Assert.DoesNotContain("#", m.Value, StringComparison.Ordinal);
            Assert.Contains("var(--code-tok-", m.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CodeRowHeightVariable_MatchesTheVirtualizeRowSize() {
        string css = await SheetFetch.AllAsync(f.CreateClient());
        var m = CodeRowHeightRegex().Match(css);
        Assert.True(m.Success, "--code-row-h not found in the compiled sheet");
        float declared = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.Equal(CodeMetrics.RowHeightPx, declared);
    }

    [Fact]
    public async Task DiffRowHeightVariable_IsGone() {
        string css = await SheetFetch.AllAsync(f.CreateClient());
        Assert.DoesNotContain("--diff-row-h", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetiredCodeClasses_AreGoneFromTheSheet() {
        string css = await SheetFetch.AllAsync(f.CreateClient());
        foreach (string cls in new[] {
                     ".ctv-row", ".ctv-num", ".ctv-line", ".ctv-note",
                     ".dsp-row", ".dsp-txt", ".duni-row", ".duni-head",
                     ".pdiff-badge", ".pdiff-entry", ".jv-string", ".jv-boolean"
                 }) {
            Assert.DoesNotContain(cls, css, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CodeWrapRule_NeverBreaksOnAHyphen() {
        string css = await SheetFetch.AllAsync(f.CreateClient());
        var m = CodeWrapRuleRegex().Match(css);
        Assert.True(m.Success, ".code-wrap .code-line rule not found");
        Assert.Contains("overflow-wrap: break-word", m.Value, StringComparison.Ordinal);
        Assert.Contains("word-break: normal", m.Value, StringComparison.Ordinal);
        Assert.Contains("hyphens: none", m.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("anywhere", m.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeSurfaceClasses_AreDefined() {
        string css = await SheetFetch.AllAsync(f.CreateClient());
        foreach (string cls in new[] {
                     ".code-surface", ".code-toolbar", ".code-scroll", ".code-rows", ".code-row",
                     ".code-toolbar-gap", ".code-lang", ".code-select", ".code-filter", ".code-toggle-on",
                     ".code-gutter", ".code-line", ".code-note", ".code-wrap", ".code-mark",
                     ".cdiff", ".cdiff-row", ".cdiff-gutter", ".cdiff-sign", ".cdiff-text",
                     ".cdiff-ctx", ".cdiff-add", ".cdiff-rem", ".cdiff-chg", ".cdiff-head",
                     ".cdiff-meta", ".cdiff-ink-add", ".cdiff-ink-rem",
                     ".cstruct", ".cstruct-summary", ".cstruct-entry", ".cstruct-head",
                     ".cstruct-path", ".cstruct-rows", ".cstruct-add", ".cstruct-rem"
                 }) {
            Assert.Contains(cls, css, StringComparison.Ordinal);
        }
    }

    [GeneratedRegex(@"--code-row-h:\s*([0-9.]+)px")]
    private static partial Regex CodeRowHeightRegex();

    [GeneratedRegex(@"\.code-wrap(\[b-[a-z0-9]+\])? \.code-line(\[b-[a-z0-9]+\])?\s*\{[^}]*\}", RegexOptions.Multiline)]
    private static partial Regex CodeWrapRuleRegex();
}
