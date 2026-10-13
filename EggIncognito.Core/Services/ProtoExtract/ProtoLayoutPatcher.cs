using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services.ProtoExtract;

public static partial class ProtoLayoutPatcher {
    public sealed record Result(bool Ok, string? Text, string? Error);

    private sealed record Block(string Key, string Parent, int Start, int End);

    private sealed class Layout {
        public required List<string> Lines { get; init; }
        public Dictionary<string, Block> Blocks { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<int, int>> Members { get; } = new(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"^(message|enum|oneof)\s+(\w+)\s*\{")]
    private static partial Regex OpenRe();

    [GeneratedRegex(@"=\s*(-?\d+)\s*[;\[]")]
    private static partial Regex NumberRe();

    [GeneratedRegex(@"\b(ei|aux)\.")]
    private static partial Regex NamespaceRe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRe();

    public static Result Apply(string baseText, string targetText) {
        var mine = Scan(baseText);
        var theirs = Scan(targetText);
        var replace = new Dictionary<int, string?>();
        var insert = new Dictionary<int, List<string>>();

        foreach (var block in mine.Blocks.Values) {
            if (theirs.Blocks.ContainsKey(block.Key)) continue;
            if (block.Parent.Length > 0 && !theirs.Blocks.ContainsKey(block.Parent)) continue;
            int from = block.Start > 0 && mine.Lines[block.Start - 1].Trim().Length == 0 ? block.Start - 1 : block.Start;
            for (int i = from; i <= block.End; i++) replace[i] = null;
        }

        foreach (var block in theirs.Blocks.Values.OrderBy(b => b.Start)) {
            if (mine.Blocks.ContainsKey(block.Key)) continue;
            if (block.Parent.Length > 0 && !mine.Blocks.ContainsKey(block.Parent)) continue;
            int at = block.Parent.Length == 0 ? mine.Lines.Count : mine.Blocks[block.Parent].End;
            var body = new List<string>();
            if (at > 0 && mine.Lines[at - 1].Trim() is { Length: > 0 } prev && !prev.EndsWith('{')) body.Add("");
            body.AddRange(theirs.Lines[block.Start..(block.End + 1)]);
            Insert(insert, at, body);
        }

        foreach (var (key, theirMembers) in theirs.Members) {
            if (!mine.Members.TryGetValue(key, out var myMembers) || !mine.Blocks.TryGetValue(key, out var block)) continue;
            foreach (var (number, ti) in theirMembers) {
                string want = theirs.Lines[ti].Trim();
                if (myMembers.TryGetValue(number, out int mi)) {
                    if (!string.Equals(Norm(mine.Lines[mi]), Norm(want), StringComparison.Ordinal))
                        replace[mi] = Indent(mine.Lines[mi]) + want;
                    continue;
                }

                int anchor = myMembers.Count > 0 ? myMembers.Values.Max() : block.Start;
                string indent = myMembers.Count > 0 ? Indent(mine.Lines[anchor]) : Indent(mine.Lines[block.Start]) + "    ";
                Insert(insert, anchor + 1, [indent + want]);
            }

            foreach (var (number, mi) in myMembers) {
                if (!theirMembers.ContainsKey(number)) replace[mi] = null;
            }
        }

        string patched = Render(mine.Lines, replace, insert, baseText.EndsWith('\n'));
        if (!string.Equals(Signature(Scan(patched)), Signature(theirs), StringComparison.Ordinal))
            return new Result(false, null, "layout patch did not reproduce the new proto");
        var norm = ProtoCanonicalForm.Normalize(patched);
        return norm.Ok
            ? new Result(true, patched, null)
            : new Result(false, null, "patched proto does not compile: " + norm.Error);
    }

    private static Layout Scan(string text) {
        var layout = new Layout { Lines = Lines(text) };
        var stack = new List<(string Kind, string Key, int Start)>();
        for (int i = 0; i < layout.Lines.Count; i++) {
            string t = layout.Lines[i].Trim();
            string owner = stack.Count == 0 ? "" : stack[^1].Key;
            var open = OpenRe().Match(t);
            if (open.Success) {
                string kind = open.Groups[1].Value;
                string scope = MessageScope(stack);
                string key = kind switch {
                    "message" => Join(scope, open.Groups[2].Value),
                    "enum" => "enum " + Join(scope, open.Groups[2].Value),
                    _ => owner
                };
                if (kind != "oneof") layout.Members.TryAdd(key, []);
                stack.Add((kind, key, i));
                continue;
            }

            if (t.StartsWith('}') && stack.Count > 0) {
                var top = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                if (top.Kind != "oneof") layout.Blocks.TryAdd(top.Key, new Block(top.Key, MessageScope(stack), top.Start, i));
                continue;
            }

            if (owner.Length == 0 || t.StartsWith("//", StringComparison.Ordinal)
                                  || t.StartsWith("option", StringComparison.Ordinal)
                                  || t.StartsWith("reserved", StringComparison.Ordinal)) continue;
            var number = NumberRe().Match(t);
            if (number.Success)
                layout.Members[owner].TryAdd(int.Parse(number.Groups[1].Value, CultureInfo.InvariantCulture), i);
        }

        return layout;
    }

    private static string MessageScope(List<(string Kind, string Key, int Start)> stack) {
        for (int i = stack.Count - 1; i >= 0; i--) {
            if (stack[i].Kind == "message") return stack[i].Key;
        }

        return "";
    }

    private static string Join(string scope, string name) => scope.Length == 0 ? name : scope + "." + name;

    private static string Norm(string line) => SpaceRe().Replace(NamespaceRe().Replace(line.Trim(), ""), " ");

    private static string Indent(string line) => line[..(line.Length - line.TrimStart().Length)];

    private static void Insert(Dictionary<int, List<string>> insert, int at, IEnumerable<string> lines) {
        if (!insert.TryGetValue(at, out var list)) insert[at] = list = [];
        list.AddRange(lines);
    }

    private static string Signature(Layout layout) =>
        string.Join("\n", layout.Members.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv =>
            kv.Key + ":" + string.Join("|", kv.Value.OrderBy(m => m.Key).Select(m => Norm(layout.Lines[m.Value])))));

    private static List<string> Lines(string text) {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static string Render(List<string> lines, Dictionary<int, string?> replace,
        Dictionary<int, List<string>> insert, bool trailingNewline) {
        var sb = new StringBuilder();
        for (int i = 0; i <= lines.Count; i++) {
            if (insert.TryGetValue(i, out var added)) {
                foreach (string a in added) sb.Append(a).Append('\n');
            }

            if (i == lines.Count) break;
            if (!replace.TryGetValue(i, out string? r)) sb.Append(lines[i]).Append('\n');
            else if (r is not null) sb.Append(r).Append('\n');
        }

        return trailingNewline ? sb.ToString() : sb.ToString().TrimEnd('\n');
    }
}
