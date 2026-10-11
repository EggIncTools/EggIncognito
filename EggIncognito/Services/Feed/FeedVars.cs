using System.Text;

namespace EggIncognito.Services.Feed;

public sealed record FeedVarInfo(string Name, string Label, string Example);

public static class FeedVars {
    private const int ExampleCap = 14;

    public static IReadOnlyList<FeedVarInfo> Describe(NotificationKind kind) {
        var values = kind.Samples.Count == 0
            ? new Dictionary<string, string>()
            : kind.Samples[0].Event.Vars();
        var described = new List<FeedVarInfo>(kind.Vars.Count);
        foreach (string name in kind.Vars) {
            described.Add(new FeedVarInfo(name, Label(name),
                values.TryGetValue(name, out string? value) ? Shorten(value) : ""));
        }

        return described;
    }

    public static string Label(string name) {
        if (name.Length == 0) return name;
        var sb = new StringBuilder(name.Length + 4);
        sb.Append(char.ToUpperInvariant(name[0]));
        foreach (char c in name.AsSpan(1)) {
            if (char.IsUpper(c)) sb.Append(' ').Append(char.ToLowerInvariant(c));
            else sb.Append(c);
        }

        return sb.ToString();
    }

    private static string Shorten(string value) =>
        value.Length <= ExampleCap ? value : value[..ExampleCap] + "...";
}
