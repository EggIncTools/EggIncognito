using System.Globalization;

namespace EggIncognito.Components.Shared.Code;

public enum CodeGutter {
    None,
    Numbers,
    Labels
}

public enum CodeDiffMode {
    Split,
    Unified,
    Structured
}

public static class CodeMetrics {
    public const float RowHeightPx = 20f;
    public const int VirtualizeAbove = 500;
    public const int Overscan = 80;
    public const int WrapRowCap = 5000;
    public const int JumpLead = 3;
    public const int MinGutterChars = 2;

    public static string GutterWidth(int chars) {
        int n = Math.Max(MinGutterChars, chars);
        return string.Create(CultureInfo.InvariantCulture, $"calc({n}ch + 0.75rem)");
    }

    public static int DigitsFor(int lineCount) {
        int n = Math.Max(1, lineCount);
        int digits = 1;
        while (n >= 10) {
            n /= 10;
            digits++;
        }

        return digits;
    }

    public static int WidestLabel(IReadOnlyList<string>? labels) {
        if (labels is null || labels.Count == 0) return MinGutterChars;
        int widest = MinGutterChars;
        foreach (string label in labels) {
            if (label.Length > widest) widest = label.Length;
        }

        return widest;
    }
}
