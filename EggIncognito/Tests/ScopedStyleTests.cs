using System.Text.RegularExpressions;

namespace EggIncognito.Tests;

public partial class ScopedStyleTests {
    [Fact]
    public void ScopedSheets_CarryNoTailwindSyntax() {
        var bad = new List<string>();
        foreach (string path in ScopedSheets()) {
            string css = File.ReadAllText(path);
            if (css.Contains("@apply", StringComparison.Ordinal) || css.Contains("@theme", StringComparison.Ordinal))
                bad.Add(Path.GetFileName(path));
        }

        Assert.True(bad.Count == 0, "@apply/@theme in scoped sheets: " + string.Join(", ", bad));
    }

    [Fact]
    public void DeepCombinator_IsAnchoredOnAScopedClass() {
        var bad = new List<string>();
        foreach (string path in ScopedSheets()) {
            foreach (string line in File.ReadAllLines(path)) {
                if (UnanchoredDeepRegex().IsMatch(line)) bad.Add(Path.GetFileName(path) + ": " + line.Trim());
            }
        }

        Assert.True(bad.Count == 0, "unanchored ::deep: " + string.Join("; ", bad));
    }

    [Fact]
    public void BuilderEmittedClasses_AreNotStyledOnlyInTheEmittersOwnSheet() {
        string spanText = Path.Combine(ComponentsDir(), "Shared", "Code", "SpanText.razor.css");
        Assert.False(File.Exists(spanText) && File.ReadAllText(spanText).Trim().Length > 0,
            "SpanText renders through RenderTreeBuilder; its own scoped rules never match");
    }

    private static IEnumerable<string> ScopedSheets() =>
        Directory.EnumerateFiles(ComponentsDir(), "*.razor.css", SearchOption.AllDirectories);

    private static string ComponentsDir() => Path.Combine(FindRepoRoot(), "EggIncognito", "Components");

    private static string FindRepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root not found");
    }

    [GeneratedRegex(@"(^|[,{]\s*)::deep")]
    private static partial Regex UnanchoredDeepRegex();
}
