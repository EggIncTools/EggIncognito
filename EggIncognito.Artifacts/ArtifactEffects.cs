using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EggIncognito.Artifacts;

public static partial class ArtifactEffects {
    public const char Escape = '\u001b';
    public const string Times = "×";
    public const string DefaultCurrency = "cash";
    private const int PercentDimension = 12;
    private const int CountDimension = 20;
    private const uint AlwaysSignedDimensions = 0x20101000;
    private const int LargeThreshold = 5;

    [GeneratedRegex(@"\{(?<token>[a-z0-9]+)(?::(?<arg>[0-9]+))?\}")]
    private static partial Regex Placeholder();

    public static string Render(string template, int dimension, double magnitude, Func<int, string?> label,
        string currency = DefaultCurrency) =>
        Placeholder().Replace(template, m => Expand(m.Groups["token"].Value, m.Groups["arg"].Value, dimension,
            magnitude, label, currency));

    public static string Plain(string rendered) {
        var sb = new StringBuilder(rendered.Length);
        for (int i = 0; i < rendered.Length; i++) {
            if (rendered[i] == Escape) {
                i++;
                continue;
            }

            sb.Append(rendered[i]);
        }

        return sb.ToString();
    }

    public static string FormatValue(int dimension, double magnitude) {
        if (dimension == PercentDimension) return Round(magnitude * 100) + "%";
        if (dimension == CountDimension) return Truncate(magnitude);
        if (magnitude >= LargeThreshold) return Compact(magnitude) + Times;
        if (magnitude == 0) return "";
        return Round(magnitude * 100 - 100) + "%";
    }

    public static string ModifyPrefix(int dimension, double magnitude) {
        if (dimension is >= 0 and <= 29 && ((1u << dimension) & AlwaysSignedDimensions) != 0) return "+";
        return magnitude is >= 1 and < LargeThreshold ? "+" : "";
    }

    private static string Expand(string token, string arg, int dimension, double magnitude, Func<int, string?> label,
        string currency) {
        int dim = arg.Length > 0 ? int.Parse(arg, CultureInfo.InvariantCulture) : dimension;
        return token switch {
            "prefix" => ModifyPrefix(dim, magnitude),
            "value" => FormatValue(dim, magnitude),
            "label" => label(dim) ?? "",
            "currency" => currency,
            "comma" => Comma(magnitude),
            "int" => Truncate(magnitude),
            "int100" => Truncate(magnitude * 100),
            "number" => Compact(magnitude),
            _ => ""
        };
    }

    private static string Round(double v) =>
        ((long)Math.Round(v, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    private static string Truncate(double v) => ((long)Math.Truncate(v)).ToString(CultureInfo.InvariantCulture);

    private static string Comma(double v) => ((ulong)Math.Truncate(v)).ToString("N0", CultureInfo.InvariantCulture);

    private static string Compact(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
}
