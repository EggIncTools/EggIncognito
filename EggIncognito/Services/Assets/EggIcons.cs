using System.Globalization;
using System.Text.Json;
using Ei;

namespace EggIncognito.Services.Assets;

public static class EggIcons {
    private static Dictionary<string, string> _iconStems = [with(StringComparer.Ordinal)];

    public static void LoadFromJson(string? docJson) {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(docJson)) {
            try {
                Fill(map, docJson);
            } catch (JsonException) {
                map.Clear();
            }
        }

        _iconStems = map;
    }

    private static void Fill(Dictionary<string, string> map, string docJson) {
        using var doc = JsonDocument.Parse(docJson);
        if (!doc.RootElement.TryGetProperty("eggIcons", out var rows) || rows.ValueKind != JsonValueKind.Array) return;
        foreach (var row in rows.EnumerateArray()) {
            string? name = row.TryGetProperty("eggName", out var n) ? n.GetString() : null;
            string? stem = row.TryGetProperty("iconStem", out var st) ? st.GetString() : null;
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(stem)) map[Key(name)] = stem;
        }
    }

    public static string? Url(int egg, string? customEggId) {
        if (customEggId is { Length: > 0 } custom) return Safe(custom) ? Icon(custom + "_ce_icon") : null;
        if (!Enum.IsDefined((Egg)egg) || (Egg)egg is Egg.CustomEgg or Egg.Unknown) return null;
        string name = Name((Egg)egg);
        return Icon(_iconStems.TryGetValue(Key(name), out string? stem) ? stem : "egg_" + name.ToLowerInvariant());
    }

    private static string Key(string name) =>
        string.Concat(name.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant));

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

    public static string? Asset(string route, string name, string? query = null) =>
        Safe(name) ? $"/api/v1/data/asset/{route}?name={Uri.EscapeDataString(name)}{query}" : null;

    private static string Icon(string name) =>
        $"/api/v1/data/asset/icon?name={Uri.EscapeDataString(name)}";

    private static bool Safe(string name) =>
        name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static string Spaced(string pascal) =>
        pascal.All(char.IsUpper)
            ? pascal
            : string.Concat(pascal.SelectMany<char, char>((ch, i) => i > 0 && char.IsUpper(ch) ? [' ', ch] : [ch]));

    public static string Pretty(string? slug, bool pascal = false) {
        if (string.IsNullOrEmpty(slug)) return "";
        var words = slug.Split(['_', '-', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(pascal ? "" : " ", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }
}
