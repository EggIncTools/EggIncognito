using System.Globalization;

namespace EggIncognito.Services.Feed;

public static class FeedText {
    private const int MaxFieldChars = 900;
    private const int MaxListed = 12;

    public static string Short(string sha) => sha.Length > 12 ? sha[..12] : sha;

    public static string Or(string? value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;

    public static string Joined(IReadOnlyList<string>? items) =>
        items is null || items.Count == 0 ? "" : string.Join(", ", items);

    public static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Stamp(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public static string Unix(DateTimeOffset at) => at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Listed(IReadOnlyList<string> items) {
        var shown = items.Count <= MaxListed ? items : items.Take(MaxListed).ToList();
        string text = string.Join(", ", shown);
        if (text.Length > MaxFieldChars) text = text[..MaxFieldChars] + "...";
        int hidden = items.Count - shown.Count;
        return hidden > 0 ? text + " (+" + Count(hidden) + " more)" : text;
    }
}
