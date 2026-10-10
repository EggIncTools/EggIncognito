using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using EggIdentity.Styles.Css;
using EggIdentity.UI;
using EggIncognito.Components.Shared.Code;
using EggIncognito.Core.Services.Syntax;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public partial class StyleInvariantTests(SharedAppFactory f) {
    private static readonly string[] EngineDirectives = ["@theme", "@apply", "@layer", "@utility", "@source", "@custom-variant"];

    [Theory]
    [InlineData("/protos")]
    [InlineData("/capture")]
    public async Task Page_LinksSharedThenAppThenScoped(string path) {
        string html = await f.CreateClient().GetStringAsync(path);
        int shared = html.IndexOf(SheetFetch.SharedPath, StringComparison.Ordinal);
        int app = html.IndexOf("\"" + SheetFetch.AppPath, StringComparison.Ordinal);
        int scoped = html.IndexOf(SheetFetch.ScopedPath, StringComparison.Ordinal);
        Assert.True(shared >= 0 && app > shared && scoped > app, "expected shared.css, then app.css, then the scoped bundle");
    }

    [Fact]
    public async Task AppSheet_IsPlainCss() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        var engine = sheets.App.Containers.Concat(sheets.App.Statements)
            .Where(c => EngineDirectives.Any(d => c.StartsWith(d, StringComparison.Ordinal)))
            .ToList();
        Assert.True(engine.Count == 0, "engine-only syntax in app.css: " + string.Join(", ", engine));
    }

    [Fact]
    public async Task EveryColorTokenRead_IsDefined() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        var defined = sheets.Defined;
        var missing = sheets.All.SelectMany(s => s.ReadProperties)
            .Where(p => p.StartsWith("--color-", StringComparison.Ordinal) && !defined.Contains(p))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "color tokens read but never defined: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task AppSheet_DeclaresEveryColorTokenTheSharedLayerReads() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        var missing = sheets.Shared.ReadProperties
            .Where(p => p.StartsWith("--color-", StringComparison.Ordinal) && !sheets.App.DefinedProperties.Contains(p))
            .Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "app.css :root lacks shared palette tokens: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task SharedLayer_ShipsNoPalette() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        Assert.DoesNotContain(sheets.Shared.DefinedProperties, p => p.StartsWith("--color-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ColorLiterals_LiveOnlyInRootTokens() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        var bad = sheets.App.Rules.Concat(sheets.Scoped.Rules)
            .Where(r => r.Selector != ":root")
            .SelectMany(r => r.Declarations
                .Where(d => !d.Property.StartsWith("--", StringComparison.Ordinal))
                .Where(d => HexLiteralRegex().IsMatch(StripVarFallbacks(d.Value)))
                .Select(d => $"{r.Selector} {{ {d.Property}: {d.Value} }}"))
            .ToList();
        Assert.True(bad.Count == 0, "hex colors outside :root tokens: " + string.Join("; ", bad));
    }

    [Fact]
    public async Task BodyFont_IsTheMonoStack() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        Assert.Equal("var(--font-mono)", sheets.App.Rule("body")?["font-family"]);
        Assert.Equal("var(--font-mono)", sheets.App.Rule(":root")?["--default-font-family"]);
    }

    [Fact]
    public async Task EveryMarkupClass_ResolvesToARule() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        var defined = DefinedClasses(sheets);
        var missing = MarkupClasses().Where(c => !defined.Contains(c) && !UnstyledHooks.Contains(c))
            .Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "markup classes with no rule in any sheet: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task EveryAppSheetClass_HasAConsumer() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        string sources = SourceText();
        var shared = SelectorClasses(sheets.Shared.Selectors);
        var dead = SelectorClasses(sheets.App.Selectors)
            .Where(c => !shared.Contains(c) && !Rendered(sources, c))
            .Order(StringComparer.Ordinal).ToList();
        Assert.True(dead.Count == 0, "app.css classes nothing renders: " + string.Join(", ", dead));
    }

    [Fact]
    public async Task Motion_StopsUnderReducedMotion() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        var reduced = sheets.All.SelectMany(s => s.Within("@media (prefers-reduced-motion: reduce)"))
            .Select(r => r.Selector).ToHashSet(StringComparer.Ordinal);
        var moving = sheets.Rules
            .Where(r => r.Container is null && (r.Declares("animation") || TransitionsLayout(r)))
            .Select(r => r.Selector)
            .Where(s => s.Contains("wb-", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(moving);
        var unguarded = moving.Where(s => !reduced.Any(g => g.Split(',').Select(p => p.Trim()).Contains(s))).ToList();
        Assert.True(unguarded.Count == 0, "workbench motion without a reduced-motion override: " + string.Join(", ", unguarded));
    }

    [Fact]
    public async Task AppAndScopedSheets_PassMotionGuard() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        var violations = new[] { sheets.App, sheets.Scoped }
            .SelectMany(s => MotionGuard.Check(s, ["egi-hue", "scroll-marker"]))
            .Select(v => $"{v.Selector} {{ {v.Property}: {v.Value} }} ({v.Reason})")
            .ToList();
        Assert.True(violations.Count == 0, "motion violations: " + string.Join("; ", violations));
    }

    [Fact]
    public async Task AppSheet_DeclaresEveryMotionTokenTheSharedLayerReads() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        var missing = sheets.Shared.ReadProperties
            .Where(p => p.StartsWith("--motion-", StringComparison.Ordinal) || p.StartsWith("--ease-", StringComparison.Ordinal))
            .Where(p => !sheets.App.DefinedProperties.Contains(p))
            .Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "app.css :root lacks motion tokens the shared layer reads: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task TokenClassRules_ReadTheirCodeToken() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        foreach (string cls in TokenClasses.All) {
            var rule = sheets.App.Rule("." + cls);
            Assert.True(rule is not null, "rule not found for ." + cls);
            Assert.StartsWith("var(--code-tok-", rule["color"], StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CodeRowHeightVariable_MatchesTheVirtualizeRowSize() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        string? declared = sheets.App.Rule(":root")?["--code-row-h"];
        Assert.NotNull(declared);
        Assert.Equal(CodeMetrics.RowHeightPx, float.Parse(declared.TrimEnd('p', 'x'), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task CodeWrap_NeverBreaksOnAHyphen() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        var rule = sheets.Scoped.Rules.FirstOrDefault(r => CodeWrapSelectorRegex().IsMatch(r.Selector));
        Assert.NotNull(rule);
        Assert.Equal("break-word", rule["overflow-wrap"]);
        Assert.Equal("normal", rule["word-break"]);
        Assert.Equal("none", rule["hyphens"]);
    }

    [Fact]
    public async Task OnlyTheWorkbenchKnowsItsSize() {
        var sheets = await SheetFetch.ParsedAsync(f.CreateClient());
        var sized = sheets.App.Rules.Concat(sheets.Scoped.Rules)
            .Where(r => CardOnlySelectorRegex().IsMatch(r.Selector))
            .Where(r => r.Declares("width") || r.Declares("height") || r.Declares("max-width"))
            .Select(r => r.Selector).ToList();
        Assert.True(sized.Count == 0, "workbench cards that size themselves instead of using --wb-card-*: " + string.Join(", ", sized));
    }

    private static readonly HashSet<string> UnstyledHooks = [
        "code-toggle-gutter", "code-toggle-wrap", "detail-root", "reg-c-app", "reg-c-build", "reg-c-client",
        "theme-preview-scope"
    ];

    private static bool TransitionsLayout(CssRule r) =>
        r["transition"] is { } t && (t.Contains("width", StringComparison.Ordinal) || t.Contains("margin", StringComparison.Ordinal)
                                                                                    || t.Contains("transform", StringComparison.Ordinal));

    private static HashSet<string> DefinedClasses(SheetFetch.Sheets sheets) =>
        SelectorClasses(sheets.All.SelectMany(s => s.Selectors));

    private static HashSet<string> SelectorClasses(IEnumerable<string> selectors) =>
        selectors.SelectMany(s => SelectorClassRegex().Matches(s).Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> MarkupClasses() =>
        new[] { Path.Combine(AppDir(), "Components"), Path.Combine(AppDir(), "..", "EggIncognito.Extensibility") }
            .Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, "*.razor", SearchOption.AllDirectories))
            .Where(p => !SkippedDirRegex().IsMatch(p))
            .SelectMany(p => StaticClassAttrRegex().Matches(File.ReadAllText(p)))
            .SelectMany(m => m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(c => PlainClassRegex().IsMatch(c))
            .Distinct(StringComparer.Ordinal);

    private static bool Rendered(string sources, string cls) {
        if (Regex.IsMatch(sources, @"(?<![\w-])" + Regex.Escape(cls) + @"(?![\w-])")) return true;
        int dash = cls.LastIndexOf('-');
        return dash > 0 && sources.Contains(cls[..(dash + 1)] + "{", StringComparison.Ordinal);
    }

    private static string SourceText() {
        string app = AppDir();
        string[] roots = [app, Path.Combine(app, "..", "EggIncognito.Core"), Path.Combine(app, "..", "EggIncognito.Extensibility")];
        var files = roots.Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, "*.*", SearchOption.AllDirectories))
            .Where(p => p.EndsWith(".razor", StringComparison.Ordinal) || p.EndsWith(".cs", StringComparison.Ordinal)
                                                                         || p.EndsWith(".js", StringComparison.Ordinal))
            .Where(p => !SkippedDirRegex().IsMatch(p));
        return string.Join("\n", files.Select(File.ReadAllText).Append(UserStrings(typeof(WorkbenchRail).Assembly)));
    }

    private static string UserStrings(Assembly assembly) {
        using var pe = new PEReader(File.OpenRead(assembly.Location));
        var md = pe.GetMetadataReader();
        var sb = new StringBuilder();
        for (var h = MetadataTokens.UserStringHandle(1); !h.IsNil; h = md.GetNextHandle(h)) sb.Append(md.GetUserString(h)).Append('\n');
        return sb.ToString();
    }

    private static string StripVarFallbacks(string value) => VarFallbackRegex().Replace(value, "var($1)");

    private static string AppDir() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (dir.GetFiles("*.slnx").Length > 0) return Path.Combine(dir.FullName, "EggIncognito");
            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root not found");
    }

    [GeneratedRegex(@"[\\/](obj|bin|Tests)[\\/]")]
    private static partial Regex SkippedDirRegex();

    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b")]
    private static partial Regex HexLiteralRegex();

    [GeneratedRegex(@"var\((--[\w-]+)\s*,[^()]*(?:\([^()]*\)[^()]*)*\)")]
    private static partial Regex VarFallbackRegex();

    [GeneratedRegex(@"\.([A-Za-z][\w-]*)")]
    private static partial Regex SelectorClassRegex();

    [GeneratedRegex(@"\bclass=""([^""@]*)""")]
    private static partial Regex StaticClassAttrRegex();

    [GeneratedRegex(@"^[a-z][a-z0-9-]*$")]
    private static partial Regex PlainClassRegex();

    [GeneratedRegex(@"^\.code-wrap(\[b-[a-z0-9]+\])? \.code-line(\[b-[a-z0-9]+\])?$")]
    private static partial Regex CodeWrapSelectorRegex();

    [GeneratedRegex(@"^\.(dwb|theme-wb|awb|nwb)-card(\[b-[a-z0-9]+\])?$")]
    private static partial Regex CardOnlySelectorRegex();
}
