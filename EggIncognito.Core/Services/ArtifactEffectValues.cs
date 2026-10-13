using System.Globalization;
using System.Text.RegularExpressions;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Core.Services;

public static partial class ArtifactEffectValues {
    public const int ProphecyDimension = 26;

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"[+\-]?\d+(?:\.\d+)?%")]
    private static partial Regex PercentLiteral();

    public static double Magnitude(int dimension, ArtifactTableExtractor.RarityEffect effect) =>
        dimension == ProphecyDimension && TemplatePercent(effect.Template) is { } pct ? pct / 100 : effect.Magnitude;

    public static bool Additive(int dimension) => dimension == ProphecyDimension;

    public static string? Label(ArtifactTableExtractor.Family family) {
        var labels = family.Tiers.SelectMany(t => t.Effects).Select(e => LabelOf(e.Template)).Distinct().ToList();
        return labels is [{ } only] ? only : null;
    }

    private static double? TemplatePercent(string template) {
        var m = PercentLiteral().Match(Plain(template));
        return m.Success ? double.Parse(m.Value.TrimEnd('%'), CultureInfo.InvariantCulture) : null;
    }

    private static string? LabelOf(string template) {
        string plain = Plain(template).Trim();
        if (plain.Length == 0) return null;
        var token = Token().Match(plain);
        var pct = PercentLiteral().Match(plain);
        var value = token.Success ? token : pct;
        if (!value.Success) return null;
        string after = plain[(value.Index + value.Length)..].Trim().TrimStart('%').Trim().TrimEnd('.');
        if (after.Length > 0) return after;
        string before = plain[..value.Index].Trim();
        foreach (string verb in new[] { " increased by", " increase by", " by" }) {
            if (before.EndsWith(verb, StringComparison.Ordinal)) before = before[..^verb.Length];
        }

        before = before.Trim();
        return before.Length == 0 ? null : char.ToLowerInvariant(before[0]) + before[1..];
    }

    private static string Plain(string template) {
        var chars = new List<char>(template.Length);
        for (int i = 0; i < template.Length; i++) {
            if (template[i] == '\u001b') {
                i++;
                continue;
            }

            chars.Add(template[i]);
        }

        string s = new([.. chars]);
        return s.Replace("+g", "+", StringComparison.Ordinal).Replace("%w", "%", StringComparison.Ordinal);
    }
}
