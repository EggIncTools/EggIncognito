namespace EggIncognito.Core.Services.Syntax;

public sealed class HighlightedText {
    private static readonly Token[] NoTokens = [];

    private readonly ISyntaxTokenizer _tokenizer;
    private readonly string[] _lines;
    private readonly byte[] _entry;
    private readonly Token[]?[] _tokens;

    public HighlightedText(string? text, ISyntaxTokenizer tokenizer) {
        _tokenizer = tokenizer;
        string body = text ?? "";
        CharCount = body.Length;
        Language = tokenizer.Id;
        _lines = SplitLines(body);
        _entry = new byte[_lines.Length];
        _tokens = new Token[]?[_lines.Length];

        byte state = 0;
        for (int i = 0; i < _lines.Length; i++) {
            _entry[i] = state;
            string line = _lines[i];
            if (line.Length is 0 or > SyntaxHighlighter.MaxLineChars) continue;
            try {
                state = _tokenizer.Scan(line.AsSpan(), state, null);
            } catch {
                state = 0;
            }
        }
    }

    public string Language { get; }

    public int CharCount { get; }

    public int LineCount => _lines.Length;

    public IReadOnlyList<string> Lines => _lines;

    public string LineAt(int index) => (uint)index < (uint)_lines.Length ? _lines[index] : "";

    public byte EntryStateAt(int index) => (uint)index < (uint)_entry.Length ? _entry[index] : (byte)0;

    public IReadOnlyList<Token> TokensFor(int index) {
        if ((uint)index >= (uint)_lines.Length) return NoTokens;
        var cached = _tokens[index];
        if (cached is not null) return cached;

        string line = _lines[index];
        Token[] built;
        if (line.Length == 0) {
            built = NoTokens;
        } else if (line.Length > SyntaxHighlighter.MaxLineChars) {
            built = [new Token(0, line.Length, TokenKind.Plain)];
        } else {
            var sink = new List<Token>();
            try {
                _tokenizer.Scan(line.AsSpan(), _entry[index], sink);
                built = sink.Count == 0 ? NoTokens : [.. sink];
            } catch {
                built = [new Token(0, line.Length, TokenKind.Plain)];
            }
        }

        _tokens[index] = built;
        return built;
    }

    public List<Span> SpansFor(int index) {
        var tokens = TokensFor(index);
        return [.. tokens
            .Where(t => t.Length > 0)
            .Select(t => new Span(t.Start, t.Length, TokenClasses.For(t.Kind)))];
    }

    private static string[] SplitLines(string text) {
        if (text.Length == 0) return [""];
        int count = 1;
        for (int i = 0; i < text.Length; i++) {
            if (text[i] == '\n') count++;
        }

        var lines = new string[count];
        int idx = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++) {
            if (text[i] != '\n') continue;
            int end = i > start && text[i - 1] == '\r' ? i - 1 : i;
            lines[idx++] = text[start..end];
            start = i + 1;
        }

        int lastEnd = text.Length;
        if (lastEnd > start && text[lastEnd - 1] == '\r') lastEnd--;
        lines[idx] = text[start..lastEnd];
        return lines;
    }
}
