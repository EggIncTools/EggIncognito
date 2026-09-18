namespace EggIncognito.Core.Services;

public static class ProtoEnumIndex {
    private sealed class Frame {
        public string? Kind;
        public string? Name;
        public Dictionary<int, string>? EnumMembers;
    }

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> Parse(string protoText) {
        var enums = new Dictionary<string, IReadOnlyDictionary<int, string>>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(protoText)) return enums;

        var tokens = Tokenize(protoText);
        string? package = null;
        var stack = new List<Frame>();
        string? pendingKind = null;
        string? pendingName = null;
        int bracketDepth = 0;

        for (int i = 0; i < tokens.Count; i++) {
            string tok = tokens[i];
            switch (tok) {
                case "package":
                    package = ReadDotted(tokens, i + 1);
                    break;
                case "message":
                case "enum":
                    pendingKind = tok;
                    pendingName = i + 1 < tokens.Count ? tokens[i + 1] : null;
                    break;
                case "{": {
                        var frame = new Frame { Kind = pendingKind, Name = pendingName };
                        if (pendingKind == "enum") {
                            var members = new Dictionary<int, string>();
                            frame.EnumMembers = members;
                            stack.Add(frame);
                            enums[FullName(package, stack)] = members;
                        } else {
                            stack.Add(frame);
                        }

                        pendingKind = null;
                        pendingName = null;
                        break;
                    }
                case "}":
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    break;
                case "[":
                    bracketDepth++;
                    break;
                case "]":
                    if (bracketDepth > 0) bracketDepth--;
                    break;
                default:
                    if (bracketDepth == 0 && stack.Count > 0 && stack[^1].EnumMembers is { } dict
                        && tok != "option" && tok != "reserved"
                        && i + 2 < tokens.Count && tokens[i + 1] == "="
                        && int.TryParse(tokens[i + 2], out int number)) {
                        dict.TryAdd(number, tok);
                    }

                    break;
            }
        }

        return enums;
    }

    private static string FullName(string? package, List<Frame> stack) {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(package)) parts.Add(package);
        parts.AddRange(stack.Select(f => f.Name).Where(n => !string.IsNullOrEmpty(n))!);
        return string.Join(".", parts);
    }

    private static string? ReadDotted(List<string> tokens, int start) {
        var sb = new System.Text.StringBuilder();
        for (int i = start; i < tokens.Count; i++) {
            string t = tokens[i];
            if (t is ";" or "{" or "}") break;
            sb.Append(t);
        }

        string s = sb.ToString();
        return s.Length == 0 ? null : s;
    }

    private static List<string> Tokenize(string s) {
        var tokens = new List<string>();
        int i = 0;
        int n = s.Length;
        const string punct = "{};=[]()<>,";
        while (i < n) {
            char c = s[i];
            if (char.IsWhiteSpace(c)) {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < n && s[i + 1] == '/') {
                while (i < n && s[i] != '\n') i++;
                continue;
            }

            if (c == '/' && i + 1 < n && s[i + 1] == '*') {
                i += 2;
                while (i + 1 < n && !(s[i] == '*' && s[i + 1] == '/')) i++;
                i += 2;
                continue;
            }

            if (c is '"' or '\'') {
                int j = i + 1;
                while (j < n && s[j] != c) {
                    if (s[j] == '\\') j++;
                    j++;
                }

                int end = Math.Min(j + 1, n);
                tokens.Add(s[i..end]);
                i = end;
                continue;
            }

            if (punct.Contains(c)) {
                tokens.Add(c.ToString());
                i++;
                continue;
            }

            int k = i;
            while (k < n && !char.IsWhiteSpace(s[k]) && !punct.Contains(s[k]) && s[k] is not ('"' or '\'')
                   && !(s[k] == '/' && k + 1 < n && s[k + 1] is '/' or '*')) {
                k++;
            }

            tokens.Add(s[i..k]);
            i = k;
        }

        return tokens;
    }
}
