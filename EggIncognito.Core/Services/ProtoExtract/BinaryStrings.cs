using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class BinaryStrings {
    public static string ReadCstr(byte[] bin, IBinaryImage? img, ulong va, int maxLen = int.MaxValue) {
        if (img is null || !img.TryVaToFileOffset(va, out int fo, out _)) return "";
        return ReadAt(bin, fo, maxLen);
    }

    public static string ReadCstr(byte[] bin, IReadOnlyList<MachoSections.Section> sections, ulong va,
        IReadOnlyList<string> allowedSections, int maxLen = int.MaxValue) {
        if (!MachoSections.TryVaToFileOffset(sections, va, out int fo, out var owner)) return "";
        if (!IsAllowed(owner.Name, allowedSections)) return "";
        return ReadAt(bin, fo, maxLen);
    }

    public static bool IsStringSection(string? sectionName) =>
        sectionName is "__cstring" or ".rodata" or ".data.rel.ro" or "__const";

    public static string? IsName(string s, string allowedPunctuation, bool allowDigitStart = false) {
        if (s.Length < 2) return null;
        if (!char.IsAsciiLetterUpper(s[0]) && !(allowDigitStart && char.IsAsciiDigit(s[0]))) return null;
        foreach (char c in s) {
            if (!char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && !allowedPunctuation.Contains(c))
                return null;
        }

        return s;
    }

    private static bool IsAllowed(string sectionName, IReadOnlyList<string> allowedSections) {
        foreach (string s in allowedSections) {
            if (string.Equals(s, sectionName, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static string ReadAt(byte[] bin, int fo, int maxLen) {
        int end = fo;
        while (end < bin.Length && bin[end] != 0 && end - fo < maxLen) end++;
        return Encoding.UTF8.GetString(bin, fo, end - fo);
    }
}
