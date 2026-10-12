using System.Text;

namespace EggIncognito.Services.Feed;

public sealed record FeedVarInfo(string Name, string Label, string Example);

public static class FeedVars {
    public static IReadOnlyList<FeedVarInfo> Describe(NotificationKind kind, IReadOnlyList<FeedSample> samples) {
        var values = samples.Count == 0 ? new Dictionary<string, string>() : samples[0].Event.Vars();
        return [.. kind.Vars.Select(name => new FeedVarInfo(name, Label(name), values.GetValueOrDefault(name) ?? ""))];
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
}
