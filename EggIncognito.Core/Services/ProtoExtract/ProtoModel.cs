using System.Globalization;
using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services.ProtoExtract;

public sealed record ProtoField(string Label, string Type, string Name, int Number, string Raw);

public sealed record ProtoEnumValue(string Name, int Number, string Raw);

public sealed record ProtoEnumDef(string Name, List<ProtoEnumValue> Values);

public sealed class ProtoMessage {
    public required string Name { get; init; }
    public required string Path { get; init; }
    public List<ProtoField> Fields { get; } = [];
    public List<ProtoEnumDef> Enums { get; } = [];
    public List<ProtoMessage> Children { get; } = [];
    public List<string> BodyLines { get; } = [];
}

public static partial class ProtoModelParser {
    [GeneratedRegex(@"^message\s+(\w+)\s*\{")]
    private static partial Regex MessageOpenRe();

    [GeneratedRegex(@"^enum\s+(\w+)\s*\{")]
    private static partial Regex EnumOpenRe();

    [GeneratedRegex(@"^oneof\s+\w+\s*\{")]
    private static partial Regex OneofOpenRe();

    [GeneratedRegex(@"^(optional|required|repeated)\s+([\w.]+)\s+(\w+)\s*=\s*(\d+)")]
    private static partial Regex LabeledFieldRe();

    [GeneratedRegex(@"^(map\s*<[^>]+>)\s+(\w+)\s*=\s*(\d+)")]
    private static partial Regex MapFieldRe();

    [GeneratedRegex(@"^([\w.]+)\s+(\w+)\s*=\s*(\d+)\s*;")]
    private static partial Regex BareFieldRe();

    [GeneratedRegex(@"^(\w+)\s*=\s*(-?\d+)\s*;")]
    private static partial Regex EnumValueRe();

    [GeneratedRegex(@"\b(ei|aux)\.")]
    private static partial Regex NamespaceRe();

    [GeneratedRegex(@"^(option|reserved|extensions)\b")]
    private static partial Regex SkippedKeywordRe();

    public static string NormalizeType(string type) => NamespaceRe().Replace(type, "");

    private enum ScopeKind { Message, Enum, Oneof }

    private sealed class Scope {
        public required ScopeKind Kind { get; init; }
        public ProtoMessage? Message { get; init; }
        public ProtoEnumDef? Enum { get; init; }
        public required int Depth { get; init; }
    }

    public static List<ProtoMessage> Parse(string protoText) {
        var roots = new List<ProtoMessage>();
        var scopes = new List<Scope>();
        int depth = 0;

        foreach (string rawLine in SplitLines(protoText)) {
            string trimmed = rawLine.Trim();

            var innermostMessage = InnermostMessage(scopes);

            var messageMatch = MessageOpenRe().Match(trimmed);
            if (messageMatch.Success) {
                string name = messageMatch.Groups[1].Value;
                var parent = innermostMessage;
                var created = new ProtoMessage {
                    Name = name,
                    Path = parent is null ? name : parent.Path + "." + name
                };
                if (parent is null) roots.Add(created);
                else parent.Children.Add(created);
                created.BodyLines.Add(rawLine);
                scopes.Add(new Scope { Kind = ScopeKind.Message, Message = created, Depth = depth });
                depth += CountChar(trimmed, '{') - CountChar(trimmed, '}');
                continue;
            }

            var enumMatch = EnumOpenRe().Match(trimmed);
            if (enumMatch.Success && innermostMessage is not null) {
                var createdEnum = new ProtoEnumDef(enumMatch.Groups[1].Value, []);
                innermostMessage.Enums.Add(createdEnum);
                innermostMessage.BodyLines.Add(rawLine);
                scopes.Add(new Scope { Kind = ScopeKind.Enum, Enum = createdEnum, Depth = depth });
                depth += CountChar(trimmed, '{') - CountChar(trimmed, '}');
                continue;
            }

            var oneofMatch = OneofOpenRe().Match(trimmed);
            if (oneofMatch.Success && innermostMessage is not null) {
                innermostMessage.BodyLines.Add(rawLine);
                scopes.Add(new Scope { Kind = ScopeKind.Oneof, Depth = depth });
                depth += CountChar(trimmed, '{') - CountChar(trimmed, '}');
                continue;
            }

            if (trimmed == "}") {
                depth -= 1;
                if (scopes.Count > 0 && scopes[^1].Depth == depth) {
                    var closed = scopes[^1];
                    scopes.RemoveAt(scopes.Count - 1);
                    if (closed.Kind == ScopeKind.Message && closed.Message is not null) {
                        closed.Message.BodyLines.Add(rawLine);
                        var newParent = InnermostMessage(scopes);
                        newParent?.BodyLines.AddRange(closed.Message.BodyLines);
                    } else {
                        InnermostMessage(scopes)?.BodyLines.Add(rawLine);
                    }
                } else {
                    InnermostMessage(scopes)?.BodyLines.Add(rawLine);
                }

                continue;
            }

            depth += CountChar(trimmed, '{') - CountChar(trimmed, '}');

            var currentScope = scopes.Count > 0 ? scopes[^1] : null;

            if (currentScope is { Kind: ScopeKind.Enum, Enum: { } en }) {
                var enumValueMatch = EnumValueRe().Match(trimmed);
                if (enumValueMatch.Success) {
                    en.Values.Add(new ProtoEnumValue(
                        enumValueMatch.Groups[1].Value,
                        int.Parse(enumValueMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                        rawLine.TrimEnd('\n', '\r')));
                }

                innermostMessage?.BodyLines.Add(rawLine);
                continue;
            }

            if (innermostMessage is not null
                && (currentScope is null or { Kind: ScopeKind.Message } or { Kind: ScopeKind.Oneof })) {
                TryParseField(trimmed, rawLine, innermostMessage);
            }

            innermostMessage?.BodyLines.Add(rawLine);
        }

        return roots;
    }

    private static void TryParseField(string trimmed, string rawLine, ProtoMessage message) {
        if (SkippedKeywordRe().IsMatch(trimmed)) return;

        string raw = rawLine.TrimEnd('\n', '\r');

        var labeled = LabeledFieldRe().Match(trimmed);
        if (labeled.Success) {
            message.Fields.Add(new ProtoField(
                labeled.Groups[1].Value,
                labeled.Groups[2].Value,
                labeled.Groups[3].Value,
                int.Parse(labeled.Groups[4].Value, CultureInfo.InvariantCulture),
                raw));
            return;
        }

        var map = MapFieldRe().Match(trimmed);
        if (map.Success) {
            string collapsedType = WhitespaceRe().Replace(map.Groups[1].Value, " ");
            message.Fields.Add(new ProtoField(
                "",
                collapsedType,
                map.Groups[2].Value,
                int.Parse(map.Groups[3].Value, CultureInfo.InvariantCulture),
                raw));
            return;
        }

        var bare = BareFieldRe().Match(trimmed);
        if (bare.Success) {
            message.Fields.Add(new ProtoField(
                "",
                bare.Groups[1].Value,
                bare.Groups[2].Value,
                int.Parse(bare.Groups[3].Value, CultureInfo.InvariantCulture),
                raw));
        }
    }

    private static ProtoMessage? InnermostMessage(List<Scope> scopes) {
        for (int i = scopes.Count - 1; i >= 0; i--) {
            if (scopes[i] is { Kind: ScopeKind.Message, Message: { } msg }) return msg;
        }

        return null;
    }

    private static int CountChar(string s, char c) {
        int n = 0;
        foreach (char ch in s) {
            if (ch == c) n++;
        }

        return n;
    }

    private static List<string> SplitLines(string text) {
        string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var result = new List<string>();
        int start = 0;
        for (int i = 0; i < normalized.Length; i++) {
            if (normalized[i] == '\n') {
                result.Add(normalized[start..(i + 1)]);
                start = i + 1;
            }
        }

        if (start < normalized.Length) result.Add(normalized[start..]);
        return result;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRe();
}
