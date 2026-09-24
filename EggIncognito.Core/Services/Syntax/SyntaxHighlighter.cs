using System.Collections.Frozen;
using EggIncognito.Core.Services.Syntax.Tokenizers;

namespace EggIncognito.Core.Services.Syntax;

public readonly record struct LanguageOption(string Id, string Label);

public static class SyntaxHighlighter {
    public const int MaxLineChars = 4000;
    public const int MaxDocumentChars = 8_000_000;
    public const string Fallback = "text";

    private static readonly ISyntaxTokenizer[] Registered = [
        new TextTokenizer(),
        new JsonTokenizer(),
        new YamlTokenizer(),
        new XmlTokenizer(),
        new JsTokenizer(),
        new HexTokenizer(),
        new BinTokenizer(),
        new ProtoTokenizer(),
        new DiffTokenizer(),
        new CsharpTokenizer(),
        new BashTokenizer(),
        new SqlTokenizer(),
        new HttpTokenizer(),
        new CssTokenizer(),
        new MarkdownTokenizer()
    ];

    private static readonly FrozenDictionary<string, ISyntaxTokenizer> Registry =
        Registered.ToFrozenDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> AliasMap = new Dictionary<string, string> {
        ["txt"] = "text",
        ["plain"] = "text",
        ["plaintext"] = "text",
        ["log"] = "text",
        ["json-tree"] = "json",
        ["jsonc"] = "json",
        ["yml"] = "yaml",
        ["html"] = "xml",
        ["xhtml"] = "xml",
        ["svg"] = "xml",
        ["javascript"] = "js",
        ["jsobj"] = "js",
        ["ts"] = "js",
        ["typescript"] = "js",
        ["mjs"] = "js",
        ["binary"] = "bin",
        ["hexdump"] = "hex",
        ["protobuf"] = "proto",
        ["patch"] = "diff",
        ["cs"] = "csharp",
        ["c#"] = "csharp",
        ["dotnet"] = "csharp",
        ["sh"] = "bash",
        ["shell"] = "bash",
        ["zsh"] = "bash",
        ["console"] = "bash",
        ["postgres"] = "sql",
        ["psql"] = "sql",
        ["md"] = "markdown",
        ["curl"] = "http"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> LabelMap = new Dictionary<string, string> {
        ["text"] = "Plain text",
        ["bash"] = "Shell",
        ["bin"] = "Binary",
        ["csharp"] = "C#",
        ["css"] = "CSS",
        ["diff"] = "Diff",
        ["hex"] = "Hex",
        ["http"] = "HTTP",
        ["js"] = "JavaScript",
        ["json"] = "JSON",
        ["markdown"] = "Markdown",
        ["proto"] = "Protobuf",
        ["sql"] = "SQL",
        ["xml"] = "XML",
        ["yaml"] = "YAML"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> Languages => Registry.Keys;

    public static IReadOnlyCollection<string> Aliases => AliasMap.Keys;

    public static IReadOnlyList<LanguageOption> Options { get; } = BuildOptions();

    public static string Resolve(string? language) {
        if (string.IsNullOrWhiteSpace(language)) return Fallback;
        string id = language.Trim();
        int space = id.IndexOf(' ');
        if (space > 0) id = id[..space];
        if (Registry.TryGetValue(id, out ISyntaxTokenizer? direct)) return direct.Id;
        return AliasMap.TryGetValue(id, out string? mapped) && Registry.ContainsKey(mapped) ? mapped : Fallback;
    }

    public static bool IsKnown(string? language) => !string.IsNullOrWhiteSpace(language) && Resolve(language) != Fallback;

    public static string Label(string? language) {
        string id = Resolve(language);
        return LabelMap.GetValueOrDefault(id, id);
    }

    private static LanguageOption[] BuildOptions() {
        var rest = Registry.Keys
            .Where(k => !string.Equals(k, Fallback, StringComparison.OrdinalIgnoreCase))
            .Select(k => new LanguageOption(k, Label(k)))
            .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase);
        return [new LanguageOption(Fallback, Label(Fallback)), .. rest];
    }

    public static ISyntaxTokenizer Tokenizer(string? language) => Registry[Resolve(language)];

    public static HighlightedText Highlight(string? text, string? language) {
        string body = text ?? "";
        string id = Resolve(language);
        if (body.Length > MaxDocumentChars) id = Fallback;
        return SyntaxCache.Shared.Get(body, Registry[id]);
    }
}
