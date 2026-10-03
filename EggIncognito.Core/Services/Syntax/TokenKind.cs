namespace EggIncognito.Core.Services.Syntax;

public enum TokenKind {
    Plain,
    Key,
    String,
    Number,
    Bool,
    Null,
    Keyword,
    Type,
    Ident,
    Comment,
    Punct,
    Op,
    Tag,
    Attr,
    Meta,
    Offset,
    Byte,
    Ascii,
    Invalid
}

public static class TokenClasses {
    public const string Plain = "tok-plain";
    public const string Key = "tok-key";
    public const string String = "tok-string";
    public const string Number = "tok-number";
    public const string Bool = "tok-bool";
    public const string Null = "tok-null";
    public const string Keyword = "tok-keyword";
    public const string Type = "tok-type";
    public const string Ident = "tok-ident";
    public const string Comment = "tok-comment";
    public const string Punct = "tok-punct";
    public const string Op = "tok-op";
    public const string Tag = "tok-tag";
    public const string Attr = "tok-attr";
    public const string Meta = "tok-meta";
    public const string Offset = "tok-offset";
    public const string Byte = "tok-byte";
    public const string Ascii = "tok-ascii";
    public const string Invalid = "tok-invalid";

    public const string Mark = "code-mark";
    public const string Blur = "mask";

    private static readonly string[] ByKind = [
        Plain, Key, String, Number, Bool, Null, Keyword, Type, Ident, Comment,
        Punct, Op, Tag, Attr, Meta, Offset, Byte, Ascii, Invalid
    ];

    public static IReadOnlyList<string> All => ByKind;

    public static string For(TokenKind kind) {
        int i = (int)kind;
        return (uint)i < (uint)ByKind.Length ? ByKind[i] : Plain;
    }
}
