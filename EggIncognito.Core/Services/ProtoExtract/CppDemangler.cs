namespace EggIncognito.Core.Services.ProtoExtract;

public static class CppDemangler {
    public static string? Demangle(string mangled) {
        if (string.IsNullOrEmpty(mangled)) return null;
        string s = mangled;
        if (s.StartsWith("__Z", StringComparison.Ordinal)) s = s[1..];
        if (!s.StartsWith("_Z", StringComparison.Ordinal)) return null;

        var parser = new Parser(s[2..]);
        try {
            string result = parser.ParseMangledNameBody();
            return parser.AtEnd ? result : null;
        } catch {
            return null;
        }
    }

    private sealed class DemangleFailure : Exception;

    private sealed class Parser(string text) {
        private static readonly Dictionary<char, string> Builtins = new() {
            ['v'] = "void",
            ['w'] = "wchar_t",
            ['b'] = "bool",
            ['c'] = "char",
            ['a'] = "signed char",
            ['h'] = "unsigned char",
            ['s'] = "short",
            ['t'] = "unsigned short",
            ['i'] = "int",
            ['j'] = "unsigned int",
            ['l'] = "long",
            ['m'] = "unsigned long",
            ['x'] = "long long",
            ['y'] = "unsigned long long",
            ['n'] = "__int128",
            ['o'] = "unsigned __int128",
            ['f'] = "float",
            ['d'] = "double",
            ['e'] = "long double",
            ['z'] = "...",
        };

        private static readonly Dictionary<string, string> OperatorNames = new() {
            ["cl"] = "operator()",
            ["ix"] = "operator[]",
            ["eq"] = "operator==",
            ["pl"] = "operator+",
            ["mi"] = "operator-",
            ["ml"] = "operator*",
            ["dv"] = "operator/",
            ["aS"] = "operator=",
        };

        private int _pos;
        private readonly List<string> _subs = [];

        public bool AtEnd => _pos >= text.Length;

        private char Peek() => _pos < text.Length ? text[_pos] : '\0';

        private char Next() {
            if (_pos >= text.Length) throw new DemangleFailure();
            return text[_pos++];
        }

        private void Expect(char c) {
            if (Peek() != c) throw new DemangleFailure();
            _pos++;
        }

        private bool TryConsume(string lit) {
            if (_pos + lit.Length > text.Length) return false;
            if (string.CompareOrdinal(text, _pos, lit, 0, lit.Length) != 0) return false;
            _pos += lit.Length;
            return true;
        }

        private void AddSub(string value) => _subs.Add(value);

        public string ParseMangledNameBody() {
            if (TryConsume("TI")) return "typeinfo for " + ParseType();
            if (TryConsume("TV")) return "vtable for " + ParseType();
            if (TryConsume("TS")) return "typeinfo name for " + ParseType();
            if (TryConsume("TT")) return "VTT for " + ParseType();
            return ParseEncoding();
        }

        private string ParseEncoding() {
            if (Peek() == 'Z') return ParseLocalName();

            string name = ParseName();
            if (AtEnd) return name;

            var parms = new List<string>();
            if (Peek() == 'v') {
                Next();
            } else {
                while (!AtEnd) parms.Add(ParseType());
            }

            return name + "(" + string.Join(", ", parms) + ")";
        }

        private string ParseLocalName() {
            Expect('Z');
            string inner = ParseEncoding();
            Expect('E');

            string suffix;
            if (Peek() == 's') {
                Next();
                suffix = "string literal";
            } else if (Peek() == 'd') {
                Next();
                while (char.IsAsciiDigit(Peek())) Next();
                if (Peek() == '_') Next();
                suffix = ParseUnqualifiedNameOrTemplateId(null);
            } else {
                suffix = ParseUnqualifiedNameOrTemplateId(null);
            }

            if (Peek() == '_') {
                Next();
                while (char.IsAsciiDigit(Peek())) Next();
            }

            return inner + "::" + suffix;
        }

        private string ParseName() {
            if (Peek() == 'N') {
                Next();
                SkipCvQualifiers();
                string body = ParseChainInsideNE();
                Expect('E');
                return body;
            }

            bool stdPrefix = TryConsume("St");
            string comp = ParseUnqualifiedNameOrTemplateId(null);
            string full = stdPrefix ? "std::" + comp : comp;
            AddSub(full);
            return full;
        }

        private string ParseChainInsideNE() {
            string comp0;
            bool cameFromSub;
            if (Peek() == 'S' && TryParseSubstitutionToken(out string subVal)) {
                comp0 = subVal;
                cameFromSub = true;
            } else {
                comp0 = ParseUnqualifiedNameOrTemplateId(null);
                cameFromSub = false;
            }

            if (Peek() == 'E') return comp0;
            if (!cameFromSub) AddSub(comp0);

            string cumulative = comp0;
            string priorSimple = comp0;
            while (true) {
                string comp = ParseUnqualifiedNameOrTemplateId(priorSimple);
                bool collapse = comp is "__ndk1" or "__1";
                string next = collapse ? cumulative : cumulative + "::" + comp;
                if (!collapse) priorSimple = comp;

                if (Peek() == 'E') {
                    cumulative = next;
                    break;
                }

                AddSub(next);
                cumulative = next;
            }

            return cumulative;
        }

        private bool TryParseSubstitutionToken(out string value) {
            value = "";
            if (Peek() != 'S') return false;
            int save = _pos;
            _pos++;

            if (Peek() == 't') {
                _pos++;
                value = "std";
                return true;
            }

            if (Peek() == 'a') {
                _pos++;
                value = "std::allocator";
                return true;
            }

            if (Peek() == 's') {
                _pos++;
                value = "std::string";
                return true;
            }

            if (Peek() == '_') {
                _pos++;
                value = LookupSub(0);
                return true;
            }

            if (char.IsAsciiDigit(Peek()) || char.IsAsciiLetterUpper(Peek())) {
                int idx = 0;
                while (char.IsAsciiDigit(Peek()) || char.IsAsciiLetterUpper(Peek())) {
                    char ch = Next();
                    int digit = char.IsAsciiDigit(ch) ? ch - '0' : ch - 'A' + 10;
                    idx = (idx * 36) + digit;
                }

                if (Peek() != '_') {
                    _pos = save;
                    return false;
                }

                _pos++;
                value = LookupSub(idx + 1);
                return true;
            }

            _pos = save;
            return false;
        }

        private string LookupSub(int index) {
            if (index < 0 || index >= _subs.Count) throw new DemangleFailure();
            return _subs[index];
        }

        private string ParseUnqualifiedNameOrTemplateId(string? priorSimpleNameForCtor) {
            if (Peek() == 'L') Next();
            string baseName = ParseUnqualifiedNameCore(priorSimpleNameForCtor);
            if (Peek() != 'I') return baseName;
            var args = ParseTemplateArgs();
            return baseName + "<" + string.Join(", ", args) + ">";
        }

        private string ParseUnqualifiedNameCore(string? priorSimpleNameForCtor) {
            char c = Peek();
            if (c == 'C') {
                Next();
                char variant = Next();
                if (variant is not ('1' or '2' or '3')) throw new DemangleFailure();
                return priorSimpleNameForCtor ?? throw new DemangleFailure();
            }

            if (c == 'D') {
                Next();
                char variant = Next();
                if (variant is not ('0' or '1' or '2')) throw new DemangleFailure();
                return "~" + (priorSimpleNameForCtor ?? throw new DemangleFailure());
            }

            if (c == 'U') return ParseUnnamedOrLambda();
            if (char.IsAsciiDigit(c)) return ParseSourceName();

            if (char.IsAsciiLetter(c)) {
                if (_pos + 2 <= text.Length && text[_pos] == 'c' && text[_pos + 1] == 'v') {
                    _pos += 2;
                    string targetType = ParseType();
                    return "operator " + targetType;
                }

                if (_pos + 2 <= text.Length) {
                    string code = text.Substring(_pos, 2);
                    if (OperatorNames.TryGetValue(code, out string? opName)) {
                        _pos += 2;
                        return opName;
                    }
                }
            }

            throw new DemangleFailure();
        }

        private string ParseSourceName() {
            int len = 0;
            if (!char.IsAsciiDigit(Peek())) throw new DemangleFailure();
            while (char.IsAsciiDigit(Peek())) len = (len * 10) + (Next() - '0');
            if (len <= 0 || _pos + len > text.Length) throw new DemangleFailure();
            string name = text.Substring(_pos, len);
            _pos += len;
            return name;
        }

        private string ParseUnnamedOrLambda() {
            Expect('U');
            if (Peek() == 'l') {
                Next();
                if (Peek() == 'v') {
                    Next();
                } else {
                    while (Peek() != 'E') ParseType();
                }

                Expect('E');
                while (char.IsAsciiDigit(Peek())) Next();
                Expect('_');
                return "{lambda}";
            }

            if (Peek() == 't') {
                Next();
                while (char.IsAsciiDigit(Peek())) Next();
                Expect('_');
                return "{unnamed type}";
            }

            throw new DemangleFailure();
        }

        private List<string> ParseTemplateArgs() {
            Expect('I');
            var args = new List<string>();
            while (Peek() != 'E') args.Add(ParseTemplateArg());
            Expect('E');
            return args;
        }

        private string ParseTemplateArg() {
            if (Peek() is 'X' or 'L') throw new DemangleFailure();
            return ParseType();
        }

        private string ParseType() {
            char c = Peek();
            switch (c) {
                case 'P': {
                        Next();
                        string r = ParseType() + "*";
                        AddSub(r);
                        return r;
                    }
                case 'R': {
                        Next();
                        string r = ParseType() + "&";
                        AddSub(r);
                        return r;
                    }
                case 'O': {
                        Next();
                        string r = ParseType() + "&&";
                        AddSub(r);
                        return r;
                    }
                case 'K': {
                        Next();
                        string r = ApplyConst(ParseType());
                        AddSub(r);
                        return r;
                    }
                case 'V': {
                        Next();
                        string r = ParseType() + " volatile";
                        AddSub(r);
                        return r;
                    }
                case 'F':
                    return ParseFunctionType();
                case 'N': {
                        Next();
                        SkipCvQualifiers();
                        string r = ParseChainInsideNE();
                        Expect('E');
                        AddSub(r);
                        return r;
                    }
                case 'S': {
                        if (!TryParseSubstitutionToken(out string sv)) throw new DemangleFailure();
                        return sv;
                    }
                default:
                    if (Builtins.TryGetValue(c, out string? builtin)) {
                        Next();
                        return builtin;
                    } else {
                        string comp = ParseUnqualifiedNameOrTemplateId(null);
                        AddSub(comp);
                        return comp;
                    }
            }
        }

        private string ParseFunctionType() {
            Expect('F');
            if (Peek() == 'Y') Next();
            string ret = ParseType();
            var parms = new List<string>();
            if (Peek() == 'v') {
                Next();
            } else {
                while (Peek() != 'E') parms.Add(ParseType());
            }

            if (Peek() is 'R' or 'O') Next();
            Expect('E');
            return ret + " (" + string.Join(", ", parms) + ")";
        }

        private void SkipCvQualifiers() {
            while (Peek() is 'r' or 'V' or 'K') Next();
        }

        private static string ApplyConst(string inner) =>
            inner.Length > 0 && inner[^1] is '*' or '&' ? inner + " const" : "const " + inner;
    }
}
