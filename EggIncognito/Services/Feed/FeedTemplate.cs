using System.Text.RegularExpressions;

namespace EggIncognito.Services.Feed;

public static partial class FeedTemplate {
    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TokenPattern();

    public static string Render(string template, IReadOnlyDictionary<string, string> vars) =>
        TokenPattern().Replace(template, m => vars.TryGetValue(m.Groups[1].Value, out string? v) ? v : m.Value);

    public static IReadOnlyList<string> Tokens(string? template) {
        if (string.IsNullOrEmpty(template)) return [];
        var found = new List<string>();
        foreach (Match match in TokenPattern().Matches(template)) {
            string name = match.Groups[1].Value;
            if (!found.Contains(name)) found.Add(name);
        }

        return found;
    }
}
