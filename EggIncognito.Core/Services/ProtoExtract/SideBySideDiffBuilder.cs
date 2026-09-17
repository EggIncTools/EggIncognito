namespace EggIncognito.Core.Services.ProtoExtract;

public enum DiffRowKind {
    Context,
    Added,
    Removed,
    Changed
}

public readonly record struct InkSpan(int Start, int Length);

public sealed record DiffRow(
    DiffRowKind Kind,
    int? LeftNo,
    string? Left,
    int? RightNo,
    string? Right,
    IReadOnlyList<InkSpan> LeftInk,
    IReadOnlyList<InkSpan> RightInk);

public sealed record SideBySideResult(IReadOnlyList<DiffRow> Rows, IReadOnlyList<int> HunkStarts);

public static class SideBySideDiffBuilder {
    public const int InkLineLimit = 400;

    private static readonly InkSpan[] NoInk = [];

    public static SideBySideResult Build(string aText, string bText) {
        string[] a = UnifiedDiffWriter.SplitLines(aText);
        string[] b = UnifiedDiffWriter.SplitLines(bText);
        return Build(a, b, MyersDiff.Compute(a, b, StringComparer.Ordinal));
    }

    public static SideBySideResult Build(IReadOnlyList<string> a, IReadOnlyList<string> b, IReadOnlyList<DiffOp> ops) {
        var rows = new List<DiffRow>();
        int leftNo = 0;
        int rightNo = 0;
        for (int i = 0; i < ops.Count; i++) {
            var op = ops[i];
            if (op.Kind == DiffOpKind.Equal) {
                for (int k = 0; k < op.ALength; k++) {
                    rows.Add(new DiffRow(
                        DiffRowKind.Context, ++leftNo, a[op.AStart + k], ++rightNo, b[op.BStart + k], NoInk, NoInk));
                }

                continue;
            }

            if (op.Kind == DiffOpKind.Insert) {
                for (int k = 0; k < op.BLength; k++) rows.Add(new DiffRow(DiffRowKind.Added, null, null, ++rightNo, b[op.BStart + k], NoInk, NoInk));

                continue;
            }

            int inserted = 0;
            int insertStart = 0;
            if (i + 1 < ops.Count && ops[i + 1].Kind == DiffOpKind.Insert) {
                inserted = ops[i + 1].BLength;
                insertStart = ops[i + 1].BStart;
                i++;
            }

            int paired = Math.Min(op.ALength, inserted);
            for (int k = 0; k < paired; k++) {
                string left = a[op.AStart + k];
                string right = b[insertStart + k];
                var (leftInk, rightInk) = Ink(left, right);
                rows.Add(new DiffRow(DiffRowKind.Changed, ++leftNo, left, ++rightNo, right, leftInk, rightInk));
            }

            for (int k = paired; k < op.ALength; k++) rows.Add(new DiffRow(DiffRowKind.Removed, ++leftNo, a[op.AStart + k], null, null, NoInk, NoInk));

            for (int k = paired; k < inserted; k++) rows.Add(new DiffRow(DiffRowKind.Added, null, null, ++rightNo, b[insertStart + k], NoInk, NoInk));
        }

        return new SideBySideResult(rows, FindHunkStarts(rows));
    }

    public static IReadOnlyList<string> Tokenize(string line) {
        var tokens = new List<string>();
        int i = 0;
        while (i < line.Length) {
            int start = i;
            if (IsWordChar(line[i])) {
                while (i < line.Length && IsWordChar(line[i])) i++;
            } else if (char.IsWhiteSpace(line[i])) {
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            } else {
                i++;
            }

            tokens.Add(line[start..i]);
        }

        return tokens;
    }

    private static (IReadOnlyList<InkSpan> Left, IReadOnlyList<InkSpan> Right) Ink(string left, string right) {
        if (left.Length > InkLineLimit || right.Length > InkLineLimit) return (NoInk, NoInk);

        var leftTokens = Tokenize(left);
        var rightTokens = Tokenize(right);
        var ops = MyersDiff.Compute(leftTokens, rightTokens, StringComparer.Ordinal);
        var leftOffsets = Offsets(leftTokens);
        var rightOffsets = Offsets(rightTokens);
        var leftInk = new List<InkSpan>();
        var rightInk = new List<InkSpan>();
        foreach (var op in ops) {
            if (op.Kind == DiffOpKind.Delete && HasVisibleToken(leftTokens, op.AStart, op.ALength)) {
                leftInk.Add(Span(leftOffsets, op.AStart, op.ALength));
            } else if (op.Kind == DiffOpKind.Insert && HasVisibleToken(rightTokens, op.BStart, op.BLength)) {
                rightInk.Add(Span(rightOffsets, op.BStart, op.BLength));
            }
        }

        return (leftInk, rightInk);
    }

    private static List<int> FindHunkStarts(List<DiffRow> rows) {
        var starts = new List<int>();
        bool inRun = false;
        for (int i = 0; i < rows.Count; i++) {
            if (rows[i].Kind == DiffRowKind.Context) {
                inRun = false;
                continue;
            }

            if (!inRun) {
                starts.Add(i);
                inRun = true;
            }
        }

        return starts;
    }

    private static int[] Offsets(IReadOnlyList<string> tokens) {
        var offsets = new int[tokens.Count + 1];
        for (int i = 0; i < tokens.Count; i++) offsets[i + 1] = offsets[i] + tokens[i].Length;
        return offsets;
    }

    private static InkSpan Span(int[] offsets, int start, int length) =>
        new(offsets[start], offsets[start + length] - offsets[start]);

    private static bool HasVisibleToken(IReadOnlyList<string> tokens, int start, int length) {
        for (int i = 0; i < length; i++) {
            if (!IsWhitespaceToken(tokens[start + i])) return true;
        }

        return false;
    }

    private static bool IsWhitespaceToken(string token) => token.Length > 0 && char.IsWhiteSpace(token[0]);

    private static bool IsWordChar(char c) => c is '_' or '.' || char.IsAsciiLetterOrDigit(c);
}
