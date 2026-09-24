using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Google.Protobuf.Reflection;
using FieldLabel = Google.Protobuf.Reflection.FieldDescriptorProto.Types.Label;
using FieldType = Google.Protobuf.Reflection.FieldDescriptorProto.Types.Type;

namespace EggIncognito.Core.Services.ProtoExtract;

public static partial class ProtoTextCompiler {
    private const string DescriptorFileName = "ei.proto";

    private static readonly FrozenDictionary<string, FieldType> ScalarTypes = new Dictionary<string, FieldType> {
        ["double"] = FieldType.Double,
        ["float"] = FieldType.Float,
        ["int64"] = FieldType.Int64,
        ["uint64"] = FieldType.Uint64,
        ["int32"] = FieldType.Int32,
        ["fixed64"] = FieldType.Fixed64,
        ["fixed32"] = FieldType.Fixed32,
        ["bool"] = FieldType.Bool,
        ["string"] = FieldType.String,
        ["bytes"] = FieldType.Bytes,
        ["uint32"] = FieldType.Uint32,
        ["sfixed32"] = FieldType.Sfixed32,
        ["sfixed64"] = FieldType.Sfixed64,
        ["sint32"] = FieldType.Sint32,
        ["sint64"] = FieldType.Sint64
    }.ToFrozenDictionary(StringComparer.Ordinal);

    [GeneratedRegex(@"^syntax\s*=\s*""([^""]*)""\s*;\s*$")]
    private static partial Regex SyntaxRe();

    [GeneratedRegex(@"^package\s+([A-Za-z_][\w.]*)\s*;\s*$")]
    private static partial Regex PackageRe();

    [GeneratedRegex(@"^(option|reserved|extensions|import)\b")]
    private static partial Regex SkippedKeywordRe();

    [GeneratedRegex(@"^message\s+(\w+)\s*\{\s*$")]
    private static partial Regex MessageOpenRe();

    [GeneratedRegex(@"^enum\s+(\w+)\s*\{\s*$")]
    private static partial Regex EnumOpenRe();

    [GeneratedRegex(@"^oneof\s+\w+\s*\{\s*$")]
    private static partial Regex OneofOpenRe();

    [GeneratedRegex(@"^(\w+)\s*=\s*(-?\d+)\s*(?:\[.*\])?\s*;\s*$")]
    private static partial Regex EnumValueRe();

    [GeneratedRegex(@"^(?:(optional|required|repeated)\s+)?([.\w]+)\s+(\w+)\s*=\s*(\d+)\s*(?:\[(.*)\])?\s*;\s*$")]
    private static partial Regex FieldRe();

    [GeneratedRegex(@"\bdefault\s*=\s*(.*)$")]
    private static partial Regex DefaultOptionRe();

    public static FileDescriptorProto Compile(string protoText) {
        ArgumentNullException.ThrowIfNull(protoText);

        var lines = JoinLogicalLines(SplitLines(StripComments(protoText)));
        var symbols = new Dictionary<string, SymbolKind>(StringComparer.Ordinal);
        List<MessageNode> rootMessages = [];
        List<EnumNode> rootEnums = [];
        List<Frame> frames = [];
        string syntax = "proto2";
        string package = "";

        for (int i = 0; i < lines.Count; i++) {
            (string trimmed, int lineNo) = lines[i];

            if (trimmed[0] == '}') {
                int at = 0;
                while (at < trimmed.Length && (trimmed[at] == '}' || trimmed[at] == ';' || char.IsWhiteSpace(trimmed[at]))) {
                    if (trimmed[at] == '}') {
                        if (frames.Count == 0) throw new FormatException($"line {lineNo}: unexpected '}}'");
                        frames.RemoveAt(frames.Count - 1);
                    }

                    at++;
                }

                if (at < trimmed.Length) throw new FormatException($"line {lineNo}: unparseable line '{trimmed}'");
                continue;
            }

            var syntaxMatch = SyntaxRe().Match(trimmed);
            if (syntaxMatch.Success) {
                syntax = syntaxMatch.Groups[1].Value;
                continue;
            }

            var packageMatch = PackageRe().Match(trimmed);
            if (packageMatch.Success) {
                package = packageMatch.Groups[1].Value;
                continue;
            }

            if (SkippedKeywordRe().IsMatch(trimmed)) continue;

            var messageMatch = MessageOpenRe().Match(trimmed);
            if (messageMatch.Success) {
                var parent = InnermostMessage(frames);
                string name = messageMatch.Groups[1].Value;
                var node = new MessageNode { Name = name, Full = Qualify(parent?.Full ?? RootScope(package), name) };
                symbols[node.Full] = SymbolKind.Message;
                if (parent is null) rootMessages.Add(node);
                else parent.Nested.Add(node);
                frames.Add(new Frame { Kind = FrameKind.Message, Message = node });
                continue;
            }

            var enumMatch = EnumOpenRe().Match(trimmed);
            if (enumMatch.Success) {
                var parent = InnermostMessage(frames);
                string name = enumMatch.Groups[1].Value;
                var node = new EnumNode { Name = name, Full = Qualify(parent?.Full ?? RootScope(package), name) };
                symbols[node.Full] = SymbolKind.Enum;
                if (parent is null) rootEnums.Add(node);
                else parent.Enums.Add(node);
                frames.Add(new Frame { Kind = FrameKind.Enum, Enumeration = node });
                continue;
            }

            if (OneofOpenRe().IsMatch(trimmed)) {
                frames.Add(new Frame { Kind = FrameKind.Oneof });
                continue;
            }

            if (frames.Count > 0 && frames[^1] is { Kind: FrameKind.Enum, Enumeration: { } openEnum }) {
                var valueMatch = EnumValueRe().Match(trimmed);
                if (!valueMatch.Success) throw new FormatException($"line {lineNo}: unparseable enum value '{trimmed}'");
                string rawValue = valueMatch.Groups[2].Value;
                if (!int.TryParse(rawValue, CultureInfo.InvariantCulture, out int enumNumber)) {
                    throw new FormatException($"line {lineNo}: enum value number out of range '{rawValue}'");
                }

                openEnum.Values.Add(new EnumValueNode { Name = valueMatch.Groups[1].Value, Number = enumNumber });
                continue;
            }

            var owner = InnermostMessage(frames);
            var fieldMatch = FieldRe().Match(trimmed);
            if (owner is null || !fieldMatch.Success) throw new FormatException($"line {lineNo}: unparseable line '{trimmed}'");

            string rawNumber = fieldMatch.Groups[4].Value;
            if (!int.TryParse(rawNumber, CultureInfo.InvariantCulture, out int fieldNumber)) {
                throw new FormatException($"line {lineNo}: field number out of range '{rawNumber}'");
            }

            owner.Fields.Add(new FieldNode {
                Name = fieldMatch.Groups[3].Value,
                Number = fieldNumber,
                Label = fieldMatch.Groups[1].Success ? fieldMatch.Groups[1].Value : "optional",
                RawType = fieldMatch.Groups[2].Value,
                DefaultToken = fieldMatch.Groups[5].Success ? ExtractDefaultToken(fieldMatch.Groups[5].Value) : null,
                Line = lineNo
            });
        }

        if (frames.Count > 0) throw new FormatException($"line {(lines.Count > 0 ? lines[^1].Line : 0)}: unbalanced braces, {frames.Count} block(s) never closed");
        if (rootMessages.Count == 0 && rootEnums.Count == 0) throw new FormatException("no messages or enums");

        AddPackageSymbols(package, symbols);

        var fdp = new FileDescriptorProto { Name = DescriptorFileName };
        if (package.Length > 0) fdp.Package = package;
        if (syntax.Length > 0 && !string.Equals(syntax, "proto2", StringComparison.Ordinal)) fdp.Syntax = syntax;
        foreach (var message in rootMessages) fdp.MessageType.Add(BuildMessage(message, symbols));
        foreach (var enumeration in rootEnums) fdp.EnumType.Add(BuildEnum(enumeration));
        return fdp;
    }

    private static DescriptorProto BuildMessage(MessageNode node, Dictionary<string, SymbolKind> symbols) {
        var d = new DescriptorProto { Name = node.Name };
        foreach (var field in node.Fields) d.Field.Add(BuildField(node.Full, field, symbols));
        foreach (var nested in node.Nested) d.NestedType.Add(BuildMessage(nested, symbols));
        foreach (var enumeration in node.Enums) d.EnumType.Add(BuildEnum(enumeration));
        return d;
    }

    private static EnumDescriptorProto BuildEnum(EnumNode node) {
        var e = new EnumDescriptorProto { Name = node.Name };
        foreach (var value in node.Values) {
            e.Value.Add(new EnumValueDescriptorProto { Name = value.Name, Number = value.Number });
        }

        return e;
    }

    private static FieldDescriptorProto BuildField(string scope, FieldNode field, Dictionary<string, SymbolKind> symbols) {
        var f = new FieldDescriptorProto {
            Name = field.Name,
            Number = field.Number,
            Label = field.Label switch {
                "required" => FieldLabel.Required,
                "repeated" => FieldLabel.Repeated,
                _ => FieldLabel.Optional
            }
        };

        if (ScalarTypes.TryGetValue(field.RawType, out var scalar)) {
            f.Type = scalar;
        } else {
            string? full = ResolveTypeName(scope, field.RawType, symbols);
            if (full is null || !symbols.TryGetValue(full, out var kind) || kind == SymbolKind.Package) {
                throw new FormatException($"line {field.Line}: unresolved type '{field.RawType}'");
            }

            f.Type = kind == SymbolKind.Enum ? FieldType.Enum : FieldType.Message;
            f.TypeName = full;
        }

        if (field.DefaultToken is not null) f.DefaultValue = DecodeDefault(field.DefaultToken, f.Type);
        return f;
    }

    private static string? ResolveTypeName(string scope, string type, Dictionary<string, SymbolKind> symbols) {
        if (type.Length > 0 && type[0] == '.') return symbols.ContainsKey(type) ? type : null;
        return ResolveWalk(scope, type, symbols, true) ?? ResolveWalk(scope, type, symbols, false);
    }

    private static string? ResolveWalk(string scope, string type, Dictionary<string, SymbolKind> symbols, bool commitToFirstMatch) {
        string[] parts = type.Split('.');
        string current = scope;
        while (true) {
            string candidate = Qualify(current, parts[0]);
            if (symbols.ContainsKey(candidate)) {
                string full = candidate;
                bool resolved = true;
                for (int i = 1; i < parts.Length; i++) {
                    full = Qualify(full, parts[i]);
                    if (!symbols.ContainsKey(full)) {
                        resolved = false;
                        break;
                    }
                }

                if (resolved) return full;
                if (commitToFirstMatch) return null;
            }

            if (current.Length == 0) return null;
            int cut = current.LastIndexOf('.');
            current = cut <= 0 ? "" : current[..cut];
        }
    }

    private static List<(string Text, int Line)> JoinLogicalLines(string[] raw) {
        var logical = new List<(string Text, int Line)>();
        var buf = new StringBuilder();
        int bufLine = 0;
        bool inString = false;
        for (int i = 0; i < raw.Length; i++) {
            string line = raw[i];
            for (int j = 0; j < line.Length; j++) {
                char c = line[j];
                if (inString) {
                    buf.Append(c);
                    if (c == '\\' && j + 1 < line.Length) {
                        buf.Append(line[++j]);
                        continue;
                    }

                    if (c == '"') inString = false;
                    continue;
                }

                if (char.IsWhiteSpace(c)) {
                    if (buf.Length > 0 && buf[^1] != ' ') buf.Append(' ');
                    continue;
                }

                if (buf.Length == 0) bufLine = i + 1;

                if (c == '"') inString = true;
                buf.Append(c);
                if (c is ';' or '{' or '}') {
                    string tok = buf.ToString().Trim();
                    if (tok.Length > 0) logical.Add((tok, bufLine));
                    buf.Clear();
                }
            }

            if (buf.Length > 0 && buf[^1] != ' ') buf.Append(' ');
        }

        string tail = buf.ToString().Trim();
        if (tail.Length > 0) logical.Add((tail, bufLine));
        return logical;
    }

    private static void AddPackageSymbols(string package, Dictionary<string, SymbolKind> symbols) {
        if (package.Length == 0) return;
        string accumulated = "";
        foreach (string part in package.Split('.')) {
            accumulated = Qualify(accumulated, part);
            symbols.TryAdd(accumulated, SymbolKind.Package);
        }
    }

    private static string RootScope(string package) => package.Length == 0 ? "" : "." + package;

    private static string Qualify(string scope, string name) => scope + "." + name;

    private static MessageNode? InnermostMessage(List<Frame> frames) {
        for (int i = frames.Count - 1; i >= 0; i--) {
            if (frames[i] is { Kind: FrameKind.Message, Message: { } message }) return message;
        }

        return null;
    }

    private static string? ExtractDefaultToken(string options) {
        var match = DefaultOptionRe().Match(options);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string DecodeDefault(string token, FieldType type) {
        if (type != FieldType.String) return token;
        if (token.Length < 2 || token[0] != '"' || token[^1] != '"') return token;

        string inner = token[1..^1];
        var sb = new StringBuilder(inner.Length);
        for (int i = 0; i < inner.Length; i++) {
            if (inner[i] != '\\' || i + 1 >= inner.Length) {
                sb.Append(inner[i]);
                continue;
            }

            char next = inner[++i];
            sb.Append(next switch {
                'n' => "\n",
                'r' => "\r",
                't' => "\t",
                '"' => "\"",
                '\\' => "\\",
                _ => "\\" + next
            });
        }

        return sb.ToString();
    }

    private static string StripComments(string text) {
        var sb = new StringBuilder(text.Length);
        bool inString = false;
        for (int i = 0; i < text.Length; i++) {
            char c = text[i];
            if (inString) {
                sb.Append(c);
                if (c == '\\' && i + 1 < text.Length) {
                    sb.Append(text[++i]);
                    continue;
                }

                if (c == '"') inString = false;
                continue;
            }

            if (c == '"') {
                inString = true;
                sb.Append(c);
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') {
                while (i < text.Length && text[i] != '\n') i++;
                if (i < text.Length) sb.Append('\n');
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) {
                    if (text[i] == '\n') sb.Append('\n');
                    i++;
                }

                i++;
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private enum SymbolKind { Message, Enum, Package }

    private enum FrameKind { Message, Enum, Oneof }

    private sealed class Frame {
        public required FrameKind Kind { get; init; }
        public MessageNode? Message { get; init; }
        public EnumNode? Enumeration { get; init; }
    }

    private sealed class MessageNode {
        public required string Name { get; init; }
        public required string Full { get; init; }
        public List<FieldNode> Fields { get; } = [];
        public List<MessageNode> Nested { get; } = [];
        public List<EnumNode> Enums { get; } = [];
    }

    private sealed class EnumNode {
        public required string Name { get; init; }
        public required string Full { get; init; }
        public List<EnumValueNode> Values { get; } = [];
    }

    private sealed class EnumValueNode {
        public required string Name { get; init; }
        public required int Number { get; init; }
    }

    private sealed class FieldNode {
        public required string Name { get; init; }
        public required int Number { get; init; }
        public required string Label { get; init; }
        public required string RawType { get; init; }
        public required string? DefaultToken { get; init; }
        public required int Line { get; init; }
    }
}
