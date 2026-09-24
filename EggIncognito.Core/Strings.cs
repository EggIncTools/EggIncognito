namespace EggIncognito.Core;

public static class Strings {
    public static string Truncate(string? s, int max, string marker = "") {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..Math.Max(0, max - marker.Length)] + marker;
    }
}
