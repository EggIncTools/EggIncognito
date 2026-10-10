using System.Text.RegularExpressions;
using EggIdentity.Styles.Css;

namespace EggIncognito.Tests;

internal static partial class SheetFetch {
    public const string SharedPath = "/_content/EggIdentity.UI/shared.css";
    public const string AppPath = "/app.css";
    public const string ScopedPath = "/EggIncognito.styles.css";

    public static async Task<string> AllAsync(HttpClient c) {
        string shared = await c.GetStringAsync(SharedPath);
        string app = await c.GetStringAsync(AppPath);
        string scoped = await ScopedAsync(c);
        return shared + "\n" + app + "\n" + scoped;
    }

    public static async Task<Sheets> ParsedAsync(HttpClient c) =>
        new(CssSheet.Parse(await c.GetStringAsync(SharedPath)),
            CssSheet.Parse(await c.GetStringAsync(AppPath)),
            CssSheet.Parse(await ScopedAsync(c)));

    private static async Task<string> ScopedAsync(HttpClient c) {
        string scoped = await c.GetStringAsync(ScopedPath);
        var contract = ContractImport().Match(scoped);
        return contract.Success ? scoped + "\n" + await c.GetStringAsync("/" + contract.Groups[1].Value) : scoped;
    }

    [GeneratedRegex(@"@import '(_content/EggIncognito\.Extensibility/[^']+)'")]
    private static partial Regex ContractImport();

    internal sealed record Sheets(CssSheet Shared, CssSheet App, CssSheet Scoped) {
        public IEnumerable<CssSheet> All => [Shared, App, Scoped];

        public IEnumerable<CssRule> Rules => All.SelectMany(s => s.Rules);

        public IReadOnlySet<string> Defined =>
            All.SelectMany(s => s.DefinedProperties).ToHashSet(StringComparer.Ordinal);
    }
}
