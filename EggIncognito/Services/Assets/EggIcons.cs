using System.Globalization;
using Ei;

namespace EggIncognito.Services.Assets;

public static class EggIcons {
    public static string? Url(int egg, string? customEggId) {
        if (customEggId is { Length: > 0 } custom) return Safe(custom) ? Icon(custom + "_ce_icon") : null;
        if (!Enum.IsDefined((Egg)egg) || (Egg)egg is Egg.CustomEgg or Egg.Unknown) return null;
        return Icon("egg_" + Name((Egg)egg).ToLowerInvariant());
    }

    public static string Label(int egg, string? customEggId) {
        if (customEggId is { Length: > 0 } custom) return Pretty(custom);
        if (!Enum.IsDefined((Egg)egg)) return egg.ToString(CultureInfo.InvariantCulture);
        return Spaced(Name((Egg)egg));
    }

    public static string Name(Egg egg) => egg switch {
        Egg.Immortality => "CRISPR",
        Egg.Ai => "AI",
        _ => egg.ToString()
    };

    private static string Icon(string name) =>
        $"/api/v1/data/asset/icon?name={Uri.EscapeDataString(name)}";

    private static bool Safe(string name) =>
        name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static string Spaced(string pascal) =>
        pascal.All(char.IsUpper)
            ? pascal
            : string.Concat(pascal.SelectMany<char, char>((ch, i) => i > 0 && char.IsUpper(ch) ? [' ', ch] : [ch]));

    private static string Pretty(string raw) {
        var words = raw.Replace('-', ' ').Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }
}
