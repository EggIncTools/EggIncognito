using System.Text;
using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services;

public static partial class ProtoJson {
    public static string NormalizeFloats(string json) =>
        FloatZeroRegex().Replace(json, "$1");

    public static string StripVolatile(string json) =>
        VolatileLastMemberRegex().Replace(VolatileMemberWithCommaRegex().Replace(json, ""), "");

    public static string Comparable(string json, bool live) {
        string trimmed = json.Trim();
        return NormalizeFloats(live ? StripVolatile(trimmed) : trimmed);
    }

    public static string PrettyPrint(string json) {
        var sb = new StringBuilder(json.Length * 2);
        int depth = 0, i = 0;
        bool inString = false, escape = false;
        while (i < json.Length) {
            char c = json[i++];
            if (escape) {
                sb.Append(c);
                escape = false;
                continue;
            }

            if (inString) {
                AppendInString(sb, c, ref inString, ref escape);
                continue;
            }

            AppendStructural(sb, json, c, ref i, ref depth, ref inString);
        }

        return sb.ToString();
    }

    private static void AppendInString(StringBuilder sb, char c, ref bool inString, ref bool escape) {
        sb.Append(c);
        if (c == '\\') escape = true;
        else if (c == '"') inString = false;
    }

    private static void AppendStructural(StringBuilder sb, string json, char c, ref int i, ref int depth,
        ref bool inString) {
        switch (c) {
            case ' ':
            case '\t':
            case '\r':
            case '\n': break;
            case '"':
                inString = true;
                sb.Append(c);
                break;
            case '{':
            case '[': AppendOpen(sb, json, c, ref i, ref depth); break;
            case '}':
            case ']':
                sb.AppendLine();
                sb.Append(' ', --depth * 2);
                sb.Append(c);
                break;
            case ',':
                sb.Append(c);
                sb.AppendLine();
                sb.Append(' ', depth * 2);
                break;
            case ':': sb.Append(": "); break;
            default: sb.Append(c); break;
        }
    }

    private static void AppendOpen(StringBuilder sb, string json, char open, ref int i, ref int depth) {
        sb.Append(open);
        int j = i;
        while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
        if (j < json.Length && (json[j] == '}' || json[j] == ']')) {
            sb.Append(json[j]);
            i = j + 1;
        } else {
            sb.AppendLine();
            sb.Append(' ', ++depth * 2);
        }
    }

    [GeneratedRegex(@"(?<=[:\[,\s])(-?\d+)\.0(?=[,\}\]\s\r\n])")]
    private static partial Regex FloatZeroRegex();

    [GeneratedRegex(@"""(?:serverTime|secondsRemaining)""\s*:\s*[^,}\]]*,\s*")]
    private static partial Regex VolatileMemberWithCommaRegex();

    [GeneratedRegex(@",?\s*""(?:serverTime|secondsRemaining)""\s*:\s*[^,}\]]*")]
    private static partial Regex VolatileLastMemberRegex();
}
