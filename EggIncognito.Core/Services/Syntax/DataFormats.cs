using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services.Syntax;

public readonly record struct DumpText(string Text, IReadOnlyList<string> Labels);

public static partial class DataFormats {
    public const int HexBytesPerLine = 16;
    public const int BinBytesPerLine = 8;

    public static readonly string[] JsonFormats = ["json-tree", "json", "yaml", "xml", "js"];
    public static readonly string[] ByteFormats = ["hex", "bin"];

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string> {
        ["json-tree"] = "JSON (tree)",
        ["json"] = "JSON (raw)",
        ["yaml"] = "YAML",
        ["xml"] = "XML",
        ["js"] = "JS object",
        ["hex"] = "Hex",
        ["bin"] = "Binary"
    };

    public static string Label(string fmt) => Labels.GetValueOrDefault(fmt, fmt);

    public static bool IsByteFormat(string fmt) => Array.IndexOf(ByteFormats, fmt) >= 0;

    public static string LanguageFor(string fmt) => fmt switch {
        "json-tree" => "json",
        "js" => "js",
        _ => SyntaxHighlighter.Resolve(fmt)
    };

    private static bool IsContainer(JsonNode? v) => v is JsonObject or JsonArray;

    public static string JsonToText(string? jsonStr, string fmt) {
        if (string.IsNullOrEmpty(jsonStr)) return "";
        JsonNode? value;
        try {
            value = JsonNode.Parse(jsonStr);
        } catch {
            return jsonStr;
        }

        return fmt switch {
            "json" => Reserialize(value),
            "yaml" => ToYaml(value, 0) is { Length: > 0 } y ? y : "{}",
            "xml" => PrettyXml(ToXml(value, "root")),
            "js" => ToJsLiteral(value, 0),
            _ => jsonStr
        };
    }

    private static string Reserialize(JsonNode? value) =>
        value?.ToJsonString(JsonPresets.Indented) ?? "null";

    public static string ToYaml(JsonNode? value, int indent) {
        string pad = new(' ', indent * 2);
        switch (value) {
            case null:
                return "null";
            case JsonArray arr:
                if (arr.Count == 0) return "[]";
                return string.Join("\n", arr.Select(v => {
                    string child = ToYaml(v, indent + 1);
                    return IsContainer(v) ? $"{pad}-\n{child}" : $"{pad}- {child}";
                }));
            case JsonObject obj:
                if (obj.Count == 0) return "{}";
                return string.Join("\n", obj.Select(kv => {
                    string key = YamlKey(kv.Key);
                    var v = kv.Value;
                    int childCount = v is JsonArray a ? a.Count : v is JsonObject o ? o.Count : 0;
                    return IsContainer(v) && childCount > 0
                        ? $"{pad}{key}:\n{ToYaml(v, indent + 1)}"
                        : $"{pad}{key}: {ToYaml(v, indent + 1)}";
                }));
            default:
                return ScalarYaml((JsonValue)value);
        }
    }

    private static string YamlKey(string k) =>
        BareYamlKeyRegex().IsMatch(k) ? k : JsonString(k);

    private static string ScalarYaml(JsonValue v) {
        return v.TryGetValue(out string? s) && s is not null
            ? s.Length == 0
              || YamlSpecialCharRegex().IsMatch(s)
              || EdgeWhitespaceRegex().IsMatch(s)
              || AmbiguousYamlScalarRegex().IsMatch(s)
                ? JsonString(s)
                : s
            : v.ToJsonString();
    }

    public static string ToXml(JsonNode? value, string root) => $"<{root}>{XmlBody(value)}</{root}>";

    private static string XmlBody(JsonNode? value) {
        return value switch {
            null => "",
            JsonArray arr => string.Concat(arr.Select(v => $"<item>{XmlBody(v)}</item>")),
            JsonObject obj => string.Concat(obj.Select(kv => {
                string tag = XmlTag(kv.Key);
                return $"<{tag}>{XmlBody(kv.Value)}</{tag}>";
            })),
            _ => XmlEscape(ScalarText((JsonValue)value))
        };
    }

    private static string XmlTag(string k) {
        string t = InvalidXmlTagCharRegex().Replace(k, "_");
        if (LeadingDigitOrPunctRegex().IsMatch(t)) t = "_" + t;
        return t.Length == 0 ? "_" : t;
    }

    private static string XmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static string PrettyXml(string xml) {
        var sb = new StringBuilder();
        int depth = 0;
        string[] split = xml.Replace("><", ">\n<").Split('\n');
        foreach (string node in split) {
            if (ClosingTagRegex().IsMatch(node)) depth--;
            sb.Append(new string(' ', Math.Max(0, depth) * 2)).Append(node).Append('\n');
            if (OpeningTagRegex().IsMatch(node)
                && !SelfContainedTagRegex().IsMatch(node)) {
                depth++;
            }
        }

        return sb.ToString().Trim();
    }

    public static string ToJsLiteral(JsonNode? value, int indent) {
        string pad = new(' ', indent * 2);
        string padIn = new(' ', (indent + 1) * 2);
        switch (value) {
            case null:
                return "null";
            case JsonArray arr:
                if (arr.Count == 0) return "[]";
                var items = arr.Select(v => padIn + ToJsLiteral(v, indent + 1));
                return $"[\n{string.Join(",\n", items)}\n{pad}]";
            case JsonObject obj:
                if (obj.Count == 0) return "{}";
                var rows = obj.Select(kv => $"{padIn}{JsKey(kv.Key)}: {ToJsLiteral(kv.Value, indent + 1)}");
                return $"{{\n{string.Join(",\n", rows)}\n{pad}}}";
            default:
                var jv = (JsonValue)value;
                return jv.TryGetValue(out string? s) && s is not null ? JsonString(s) : jv.ToJsonString();
        }
    }

    private static string JsKey(string k) =>
        JsIdentifierRegex().IsMatch(k) ? k : JsonString(k);

    private static string ScalarText(JsonValue v) =>
        v.TryGetValue(out string? s) && s is not null ? s : v.ToJsonString();

    private static string JsonString(string s) => JsonSerializer.Serialize(s);

    public static byte[] BytesFromBase64(string? b64) {
        if (string.IsNullOrEmpty(b64)) return [];
        try {
            return ProtoFraming.FromBase64Loose(b64);
        } catch (FormatException) {
            return [];
        }
    }

    public static DumpText HexDump(byte[] bytes) {
        if (bytes.Length == 0) return new DumpText("(empty)", [""]);
        int rows = (bytes.Length + HexBytesPerLine - 1) / HexBytesPerLine;
        var lines = new List<string>(rows);
        var labels = new List<string>(rows);
        var sb = new StringBuilder();
        for (int i = 0; i < bytes.Length; i += HexBytesPerLine) {
            int take = Math.Min(HexBytesPerLine, bytes.Length - i);
            sb.Clear();
            for (int k = 0; k < take; k++) {
                if (k > 0) sb.Append(' ');
                sb.Append(bytes[i + k].ToString("x2"));
            }

            while (sb.Length < HexBytesPerLine * 3 - 1) sb.Append(' ');
            sb.Append("  |");
            for (int k = 0; k < take; k++) {
                byte b = bytes[i + k];
                sb.Append(b is >= 32 and < 127 ? (char)b : '.');
            }

            sb.Append('|');
            labels.Add(i.ToString("x8"));
            lines.Add(sb.ToString());
        }

        return new DumpText(string.Join("\n", lines), labels);
    }

    public static DumpText BinDump(byte[] bytes) {
        if (bytes.Length == 0) return new DumpText("(empty)", [""]);
        int rows = (bytes.Length + BinBytesPerLine - 1) / BinBytesPerLine;
        var lines = new List<string>(rows);
        var labels = new List<string>(rows);
        var sb = new StringBuilder();
        for (int i = 0; i < bytes.Length; i += BinBytesPerLine) {
            int take = Math.Min(BinBytesPerLine, bytes.Length - i);
            sb.Clear();
            for (int k = 0; k < take; k++) {
                if (k > 0) sb.Append(' ');
                sb.Append(Convert.ToString(bytes[i + k], 2).PadLeft(8, '0'));
            }

            labels.Add(i.ToString("x8"));
            lines.Add(sb.ToString());
        }

        return new DumpText(string.Join("\n", lines), labels);
    }

    public static string Join(DumpText dump) {
        string[] lines = dump.Text.Split('\n');
        var sb = new StringBuilder(dump.Text.Length + lines.Length * 10);
        for (int i = 0; i < lines.Length; i++) {
            if (i > 0) sb.Append('\n');
            string label = i < dump.Labels.Count ? dump.Labels[i] : "";
            if (label.Length > 0) sb.Append(label).Append("  ");
            sb.Append(lines[i]);
        }

        return sb.ToString();
    }

    public static string ToHexDump(byte[] bytes) => Join(HexDump(bytes));

    public static string ToBinDump(byte[] bytes) => Join(BinDump(bytes));

    public static DumpText BytesToDump(string? b64, string fmt) {
        byte[] bytes = BytesFromBase64(b64);
        return fmt == "bin" ? BinDump(bytes) : HexDump(bytes);
    }

    public static string BytesToText(string? b64, string fmt) => Join(BytesToDump(b64, fmt));

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex BareYamlKeyRegex();

    [GeneratedRegex("[:#\\-?{}\\[\\],&*!|>'\"%@`]")]
    private static partial Regex YamlSpecialCharRegex();

    [GeneratedRegex("^\\s|\\s$")]
    private static partial Regex EdgeWhitespaceRegex();

    [GeneratedRegex("^(true|false|null|~|\\d)", RegexOptions.IgnoreCase)]
    private static partial Regex AmbiguousYamlScalarRegex();

    [GeneratedRegex("[^A-Za-z0-9_.-]")]
    private static partial Regex InvalidXmlTagCharRegex();

    [GeneratedRegex("^[0-9.-]")]
    private static partial Regex LeadingDigitOrPunctRegex();

    [GeneratedRegex("^</\\w")]
    private static partial Regex ClosingTagRegex();

    [GeneratedRegex("^<\\w[^>]*[^/]>$")]
    private static partial Regex OpeningTagRegex();

    [GeneratedRegex("^<.*</.*>$")]
    private static partial Regex SelfContainedTagRegex();

    [GeneratedRegex("^[A-Za-z_$][A-Za-z0-9_$]*$")]
    private static partial Regex JsIdentifierRegex();
}
