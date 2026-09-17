using EggIncognito.Core.Services.Syntax;

namespace EggIncognito.Tests;

public class SyntaxTokenizerTests {
    private static List<Token> Scan(string language, string line, byte state = 0) {
        var sink = new List<Token>();
        SyntaxHighlighter.Tokenizer(language).Scan(line.AsSpan(), state, sink);
        return sink;
    }

    private static TokenKind KindAt(List<Token> tokens, int start) {
        foreach (var t in tokens) {
            if (t.Start == start) return t.Kind;
        }

        return TokenKind.Plain;
    }

    [Theory]
    [InlineData("json")]
    [InlineData("yaml")]
    [InlineData("xml")]
    [InlineData("js")]
    [InlineData("hex")]
    [InlineData("bin")]
    [InlineData("proto")]
    [InlineData("diff")]
    [InlineData("text")]
    [InlineData("csharp")]
    [InlineData("bash")]
    [InlineData("sql")]
    [InlineData("http")]
    [InlineData("css")]
    [InlineData("markdown")]
    public void EveryMandatoryLanguage_IsRegistered(string id) {
        Assert.Equal(id, SyntaxHighlighter.Resolve(id));
        Assert.Equal(id, SyntaxHighlighter.Tokenizer(id).Id);
    }

    [Fact]
    public void DataFormatIds_AllResolveToARegisteredTokenizer() {
        foreach (string fmt in DataFormats.JsonFormats.Concat(DataFormats.ByteFormats)) {
            string language = DataFormats.LanguageFor(fmt);
            Assert.NotEqual(SyntaxHighlighter.Fallback, language);
            Assert.Equal(language, SyntaxHighlighter.Tokenizer(language).Id);
        }
    }

    [Fact]
    public void Json_KeysStringsNumbersBoolsAndNulls() {
        var tokens = Scan("json", "{\"a\": \"x\", \"b\": 12, \"c\": true, \"d\": null}");
        Assert.Equal(TokenKind.Punct, KindAt(tokens, 0));
        Assert.Equal(TokenKind.Key, KindAt(tokens, 1));
        Assert.Equal(TokenKind.String, KindAt(tokens, 6));
        Assert.Contains(tokens, t => t.Kind == TokenKind.Number);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Bool);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Null);
    }

    [Fact]
    public void Json_UnterminatedString_DoesNotRunAway() {
        var tokens = Scan("json", "{\"a\": \"unterminated");
        Assert.Contains(tokens, t => t.Kind == TokenKind.String && t.Start + t.Length == 19);
    }

    [Fact]
    public void Yaml_KeyValueAndComment() {
        var key = Scan("yaml", "name: value");
        Assert.Equal(TokenKind.Key, KindAt(key, 0));
        Assert.Equal(TokenKind.Punct, KindAt(key, 4));

        var comment = Scan("yaml", "  # note");
        Assert.Single(comment);
        Assert.Equal(TokenKind.Comment, comment[0].Kind);
    }

    [Fact]
    public void Xml_TagsAttributesAndValues() {
        var tokens = Scan("xml", "<root id=\"1\">text</root>");
        Assert.Contains(tokens, t => t.Kind == TokenKind.Tag);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Attr);
        Assert.Contains(tokens, t => t.Kind == TokenKind.String);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Plain);
    }

    [Fact]
    public void Js_KeywordsIdentifiersAndKeys() {
        var tokens = Scan("js", "const x = { a: 1 };");
        Assert.Equal(TokenKind.Keyword, KindAt(tokens, 0));
        Assert.Contains(tokens, t => t.Kind == TokenKind.Key);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Number);
    }

    [Fact]
    public void Hex_OffsetBytesAndAscii() {
        var tokens = Scan("hex", "00000000  00 01 02  |...|");
        Assert.Equal(TokenKind.Offset, tokens[0].Kind);
        Assert.Equal(8, tokens[0].Length);
        Assert.Equal(3, tokens.Count(t => t.Kind == TokenKind.Byte));
        Assert.Contains(tokens, t => t.Kind == TokenKind.Ascii);
    }

    [Fact]
    public void Hex_WithoutOffsetColumn_StillTokenizesBytes() {
        var tokens = Scan("hex", "00 01 02  |...|");
        Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.Offset);
        Assert.Equal(3, tokens.Count(t => t.Kind == TokenKind.Byte));
    }

    [Fact]
    public void Bin_GroupsOfEightBits() {
        var tokens = Scan("bin", "00000000  00000001 10000000");
        Assert.Equal(TokenKind.Offset, tokens[0].Kind);
        Assert.Equal(2, tokens.Count(t => t.Kind == TokenKind.Byte));
    }

    [Fact]
    public void Proto_KeywordsScalarTypesAndFieldNumbers() {
        var tokens = Scan("proto", "  optional string name = 3;");
        Assert.Contains(tokens, t => t.Kind == TokenKind.Keyword);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Type);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Number);
    }

    [Fact]
    public void Diff_PrefixesAndHunkHeaders() {
        Assert.Equal(TokenKind.Meta, Scan("diff", "@@ -1,3 +1,4 @@")[0].Kind);
        Assert.Equal(TokenKind.Meta, Scan("diff", "--- a/x")[0].Kind);
        Assert.Equal(TokenKind.Punct, Scan("diff", "+added")[0].Kind);
    }

    [Fact]
    public void Text_EmitsNoTokens() => Assert.Empty(Scan("text", "anything at all"));

    [Fact]
    public void Csharp_KeywordsTypesAndStrings() {
        var tokens = Scan("csharp", "public string Name = \"x\";");
        Assert.Equal(TokenKind.Keyword, KindAt(tokens, 0));
        Assert.Contains(tokens, t => t.Kind == TokenKind.Type);
        Assert.Contains(tokens, t => t.Kind == TokenKind.String);
    }

    [Fact]
    public void Bash_CommentsFlagsAndVariables() {
        Assert.Equal(TokenKind.Comment, Scan("bash", "# note")[0].Kind);
        Assert.Contains(Scan("bash", "ls -la"), t => t.Kind == TokenKind.Attr);
        Assert.Contains(Scan("bash", "echo $HOME"), t => t.Kind == TokenKind.Meta);
    }

    [Fact]
    public void Sql_KeywordsAreCaseInsensitive() {
        Assert.Contains(Scan("sql", "SELECT * FROM t"), t => t.Kind == TokenKind.Keyword);
        Assert.Contains(Scan("sql", "select * from t"), t => t.Kind == TokenKind.Keyword);
    }

    [Fact]
    public void Http_RequestLineAndHeaders() {
        var request = Scan("http", "POST /ei/first_contact HTTP/1.1");
        Assert.Equal(TokenKind.Keyword, request[0].Kind);
        Assert.Contains(request, t => t.Kind == TokenKind.String);

        var header = Scan("http", "Content-Type: application/json");
        Assert.Equal(TokenKind.Key, header[0].Kind);
    }

    [Fact]
    public void Css_SelectorsPropertiesAndValues() {
        var selector = Scan("css", ".x {");
        Assert.Equal(TokenKind.Tag, selector[0].Kind);
        var declaration = Scan("css", "  color: red;", 1);
        Assert.Equal(TokenKind.Key, declaration[0].Kind);
        Assert.Contains(declaration, t => t.Kind == TokenKind.Ident);
    }

    [Fact]
    public void Markdown_HeadingsQuotesAndInlineCode() {
        Assert.Equal(TokenKind.Keyword, Scan("markdown", "# Title")[0].Kind);
        Assert.Equal(TokenKind.Comment, Scan("markdown", "> quote")[0].Kind);
        Assert.Contains(Scan("markdown", "use `code` here"), t => t.Kind == TokenKind.String);
    }

    [Fact]
    public void UnknownLanguage_FallsBackToText() {
        Assert.Equal("text", SyntaxHighlighter.Resolve("klingon"));
        Assert.Equal("text", SyntaxHighlighter.Resolve(null));
        Assert.Equal("text", SyntaxHighlighter.Resolve("   "));
    }

    [Theory]
    [InlineData("yml", "yaml")]
    [InlineData("javascript", "js")]
    [InlineData("typescript", "js")]
    [InlineData("cs", "csharp")]
    [InlineData("sh", "bash")]
    [InlineData("zsh", "bash")]
    [InlineData("md", "markdown")]
    [InlineData("json-tree", "json")]
    [InlineData("html", "xml")]
    public void Aliases_ResolveToTheirTokenizer(string alias, string expected) => Assert.Equal(expected, SyntaxHighlighter.Resolve(alias));

    [Fact]
    public void PowershellSpellings_StayUnresolved() {
        Assert.Equal("text", SyntaxHighlighter.Resolve("ps1"));
        Assert.Equal("text", SyntaxHighlighter.Resolve("powershell"));
    }

    [Fact]
    public void NoTokenizer_UsesRegularExpressions() {
        var assembly = typeof(SyntaxHighlighter).Assembly;
        var tokenizers = assembly.GetTypes()
            .Where(t => typeof(ISyntaxTokenizer).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();
        Assert.NotEmpty(tokenizers);
        foreach (var t in tokenizers) {
            Assert.DoesNotContain(t.GetFields(System.Reflection.BindingFlags.NonPublic
                                              | System.Reflection.BindingFlags.Static
                                              | System.Reflection.BindingFlags.Instance),
                f => f.FieldType.FullName is not null
                     && f.FieldType.FullName.StartsWith("System.Text.RegularExpressions", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void LongLine_DegradesToOnePlainToken() {
        string longLine = new('a', SyntaxHighlighter.MaxLineChars + 1);
        var doc = SyntaxHighlighter.Highlight("{\"a\":1}\n" + longLine, "json");
        var tokens = doc.TokensFor(1);
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Plain, tokens[0].Kind);
        Assert.Equal(longLine.Length, tokens[0].Length);
        Assert.NotEmpty(doc.TokensFor(0));
    }

    [Fact]
    public void HugeDocument_IsForcedToText() {
        string huge = new('x', SyntaxHighlighter.MaxDocumentChars + 1);
        Assert.Equal("text", SyntaxHighlighter.Highlight(huge, "json").Language);
    }

    [Fact]
    public void ThrowingTokenizer_DegradesTheLineNotTheDocument() {
        var doc = new HighlightedText("one\ntwo", new ThrowingTokenizer());
        Assert.Equal(2, doc.LineCount);
        var tokens = doc.TokensFor(0);
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Plain, tokens[0].Kind);
        Assert.Equal(3, tokens[0].Length);
    }

    [Fact]
    public void BlockComment_TokensAreTheSameOutOfOrder() {
        const string source = "int a;\n/* one\n two\n three */\nint b;";
        var forward = SyntaxHighlighter.Highlight(source, "csharp");
        var backward = new HighlightedText(source, SyntaxHighlighter.Tokenizer("csharp"));

        var forwardKinds = new List<TokenKind>();
        for (int i = 0; i < forward.LineCount; i++) forwardKinds.AddRange(forward.TokensFor(i).Select(t => t.Kind));

        var backwardKinds = new List<List<TokenKind>>();
        for (int i = backward.LineCount - 1; i >= 0; i--) backwardKinds.Insert(0, [.. backward.TokensFor(i).Select(t => t.Kind)]);

        List<TokenKind> flattened = [.. backwardKinds.SelectMany(x => x)];
        Assert.Equal(forwardKinds, flattened);
        Assert.Equal(TokenKind.Comment, backward.TokensFor(2)[0].Kind);
    }

    [Fact]
    public void UnterminatedBlockComment_DoesNotThrowAndCarriesState() {
        var doc = SyntaxHighlighter.Highlight("/* open\nstill open\nand still", "csharp");
        for (int i = 0; i < doc.LineCount; i++) Assert.All(doc.TokensFor(i), t => Assert.Equal(TokenKind.Comment, t.Kind));
    }

    private sealed class ThrowingTokenizer : ISyntaxTokenizer {
        public string Id => "boom";

        public byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink) =>
            throw new InvalidOperationException("boom");
    }
}
