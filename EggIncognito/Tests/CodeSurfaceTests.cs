using Bunit;
using EggIncognito.Components.Shared.Code;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Core.Services.Syntax;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class CodeSurfaceTests : BunitContext {
    public CodeSurfaceTests() {
        Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void LineNumbers_RenderAsGutterElements_NotPaddedText() {
        var cut = Render<CodeSurface>(p => p
            .Add(c => c.Text, "one\ntwo\nthree")
            .Add(c => c.Language, "text"));

        var gutters = cut.FindAll(".code-gutter");
        Assert.Equal(3, gutters.Count);
        Assert.Equal("1", gutters[0].TextContent);
        Assert.Equal("3", gutters[2].TextContent);
        Assert.DoesNotContain("  1  ", cut.Find(".code-rows").TextContent);
    }

    [Fact]
    public void GutterNone_RendersNoGutterElement() {
        var cut = Render<CodeSurface>(p => p
            .Add(c => c.Text, "one\ntwo")
            .Add(c => c.Gutter, CodeGutter.None));

        Assert.Empty(cut.FindAll(".code-gutter"));
        Assert.NotEmpty(cut.FindAll(".code-nogutter"));
    }

    [Fact]
    public void GutterLabels_RenderTheSuppliedLabels() {
        var cut = Render<CodeSurface>(p => p
            .Add(c => c.Text, "aa\nbb")
            .Add(c => c.Gutter, CodeGutter.Labels)
            .Add(c => c.GutterLabels, new[] { "00000000", "00000010" }));

        var gutters = cut.FindAll(".code-gutter");
        Assert.Equal("00000000", gutters[0].TextContent);
        Assert.Equal("00000010", gutters[1].TextContent);
    }

    [Fact]
    public void FilterWithNoMatch_RendersTheNoteRow() {
        var cut = Render<CodeSurface>(p => p.Add(c => c.Text, "alpha\nbeta"));
        cut.Find(".code-filter").Input("zzz");

        var note = cut.Find(".code-note");
        Assert.Equal("(no lines match)", note.TextContent);
        Assert.Empty(cut.FindAll(".code-gutter"));
    }

    [Fact]
    public void FilterWithMatch_KeepsTheOriginalLineNumbers() {
        var cut = Render<CodeSurface>(p => p.Add(c => c.Text, "alpha\nbeta\ngamma"));
        cut.Find(".code-filter").Input("gamma");

        var gutters = cut.FindAll(".code-gutter");
        Assert.Single(gutters);
        Assert.Equal("3", gutters[0].TextContent);
    }

    [Fact]
    public void FilterHits_RenderAsMarks() {
        var cut = Render<CodeSurface>(p => p.Add(c => c.Text, "alpha\nbeta"));
        cut.Find(".code-filter").Input("lph");
        Assert.Contains("<mark>", cut.Markup);
    }

    [Fact]
    public void WrapToggle_IsDisabledAboveTheWrapRowCap() {
        string many = string.Join("\n", Enumerable.Range(0, CodeMetrics.WrapRowCap + 2).Select(i => "line " + i));
        var cut = Render<CodeSurface>(p => p.Add(c => c.Text, many));

        var toggle = cut.Find(".code-toggle-wrap");
        Assert.True(toggle.HasAttribute("disabled"));
    }

    [Fact]
    public void WrapToggle_IsEnabledBelowTheCap() {
        var cut = Render<CodeSurface>(p => p.Add(c => c.Text, "one\ntwo"));
        var toggle = cut.Find(".code-toggle-wrap");
        Assert.False(toggle.HasAttribute("disabled"));
        toggle.Click();
        Assert.NotEmpty(cut.FindAll(".code-wrap"));
    }

    [Fact]
    public void ControlBar_IsTheFirstChildOfTheSurface() {
        var cut = Render<CodeSurface>(p => p.Add(c => c.Text, "one\ntwo"));
        var surface = cut.Find(".code-surface");
        Assert.Contains("code-toolbar", surface.FirstElementChild!.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void LanguagePicker_OffersEveryRegisteredTokenizer() {
        var cut = Render<CodeSurface>(p => p.Add(c => c.Text, "one"));
        Assert.Equal(SyntaxHighlighter.Languages.Count, cut.FindAll(".code-lang option").Count);
    }

    [Fact]
    public void LanguagePicker_OverridesTheResolvedLanguage() {
        var cut = Render<CodeSurface>(p => p
            .Add(c => c.Text, "{\"a\": 1}")
            .Add(c => c.Language, "text"));

        Assert.Empty(cut.FindAll(".tok-key"));
        cut.Find(".code-lang").Change("json");
        Assert.NotEmpty(cut.FindAll(".tok-key"));
    }

    [Fact]
    public void GutterToggle_HidesTheLineNumbers() {
        var cut = Render<CodeSurface>(p => p.Add(c => c.Text, "one\ntwo"));
        cut.Find(".code-toggle-gutter").Click();
        Assert.Empty(cut.FindAll(".code-gutter"));
        Assert.NotEmpty(cut.FindAll(".code-nogutter"));
    }

    [Fact]
    public void EmptyText_RendersTheEmptyNoteInsteadOfTheSurface() {
        var cut = Render<CodeSurface>(p => p
            .Add(c => c.Text, "")
            .Add(c => c.EmptyText, "nothing here"));

        Assert.Empty(cut.FindAll(".code-surface"));
        Assert.Equal("nothing here", cut.Find(".code-note").TextContent);
    }

    [Fact]
    public void JsonText_CarriesTokenClasses() {
        var cut = Render<CodeSurface>(p => p
            .Add(c => c.Text, "{\"a\": 1}")
            .Add(c => c.Language, "json"));

        Assert.NotEmpty(cut.FindAll(".tok-key"));
        Assert.NotEmpty(cut.FindAll(".tok-number"));
    }

    [Fact]
    public void SensitiveValues_BlurAndToggleOnClick() {
        var cut = Render<CodeSurface>(p => p
            .Add(c => c.Text, "id EI1234567890 end")
            .Add(c => c.SensitiveValues, new HashSet<string>(StringComparer.Ordinal) { "EI1234567890" }));

        var masked = cut.Find(".mask");
        Assert.DoesNotContain("revealed", masked.GetAttribute("class"));
        masked.Click();
        Assert.Contains("revealed", cut.Find(".mask").GetAttribute("class"));
    }

    [Fact]
    public void ExternalFilter_SuppressesTheInternalFilterInput() {
        var cut = Render<CodeSurface>(p => p
            .Add(c => c.Text, "alpha\nbeta")
            .Add(c => c.Filter, "beta"));

        Assert.Empty(cut.FindAll(".code-filter"));
        Assert.Single(cut.FindAll(".code-gutter"));
    }

    [Fact]
    public void SplitDiff_RowsCarryBothSyntaxTokensAndInk() {
        var result = SideBySideDiffBuilder.Build("optional string name = 1;", "optional string title = 1;");
        var cut = Render<CodeDiff>(p => p
            .Add(c => c.Mode, CodeDiffMode.Split)
            .Add(c => c.Split, result)
            .Add(c => c.Language, "proto"));

        Assert.NotEmpty(cut.FindAll(".cdiff-row"));
        Assert.NotEmpty(cut.FindAll(".tok-keyword"));
        Assert.NotEmpty(cut.FindAll(".cdiff-ink-rem"));
        Assert.NotEmpty(cut.FindAll(".cdiff-ink-add"));
    }

    [Fact]
    public void UnifiedDiff_UsesTheSameRowKindVocabularyAsSplit() {
        var cut = Render<CodeDiff>(p => p
            .Add(c => c.Mode, CodeDiffMode.Unified)
            .Add(c => c.Unified, "@@ -1 +1 @@\n-old\n+new\n ctx")
            .Add(c => c.Language, "proto"));

        Assert.Single(cut.FindAll(".cdiff-head"));
        Assert.Single(cut.FindAll(".cdiff-rem"));
        Assert.Single(cut.FindAll(".cdiff-add"));
        Assert.Single(cut.FindAll(".cdiff-ctx"));
    }

    [Fact]
    public void StructuredDiff_RendersNoBadgeOrPillElement() {
        var diff = new ProtoDiffResult([
            new MessageDiff(MessageDiffKind.Added, null, "Foo", [], [], ["message Foo {", "}"]),
            new MessageDiff(MessageDiffKind.Removed, "Bar", null, [], [], ["message Bar {", "}"])
        ]);
        var cut = Render<CodeDiff>(p => p
            .Add(c => c.Mode, CodeDiffMode.Structured)
            .Add(c => c.Structural, diff));

        Assert.DoesNotContain("badge", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pill", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(cut.FindAll(".cstruct-path.cstruct-add"));
        Assert.NotEmpty(cut.FindAll(".cstruct-path.cstruct-rem"));
    }

    [Fact]
    public void StructuredDiff_EmptyResult_RendersTheEmptyText() {
        var cut = Render<CodeDiff>(p => p
            .Add(c => c.Mode, CodeDiffMode.Structured)
            .Add(c => c.Structural, new ProtoDiffResult([]))
            .Add(c => c.EmptyText, "No structural differences."));

        Assert.Contains("No structural differences.", cut.Markup);
    }
}
