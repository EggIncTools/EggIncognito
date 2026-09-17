using System.Globalization;

namespace EggIncognito.Core.Services.ProtoExtract;

public sealed record UnifiedDiffOptions(
    int Context = 3,
    string PathA = "a/ei.proto",
    string PathB = "b/ei.proto",
    string? LabelA = null,
    string? LabelB = null);

public static class UnifiedDiffWriter {
    private const string NoNewlineMarker = "\\ No newline at end of file";

    public static string[] SplitLines(string text) {
        if (string.IsNullOrEmpty(text)) return [];
        string[] raw = text.Split('\n');
        int count = raw.Length;
        if (raw[count - 1].Length == 0) count--;
        var lines = new string[count];
        for (int i = 0; i < count; i++) {
            string line = raw[i];
            lines[i] = line.Length > 0 && line[^1] == '\r' ? line[..^1] : line;
        }

        return lines;
    }

    public static string Write(IReadOnlyList<string> a, IReadOnlyList<string> b, IReadOnlyList<DiffOp> ops, UnifiedDiffOptions options)
        => Render(a, b, ops, options, true, true);

    public static string Write(string aText, string bText, UnifiedDiffOptions options) {
        string[] a = SplitLines(aText);
        string[] b = SplitLines(bText);
        return Render(a, b, MyersDiff.Compute(a, b), options, EndsWithNewline(aText), EndsWithNewline(bText));
    }

    private static bool EndsWithNewline(string text) => text.Length > 0 && text[^1] == '\n';

    private static string Render(
        IReadOnlyList<string> a,
        IReadOnlyList<string> b,
        IReadOnlyList<DiffOp> ops,
        UnifiedDiffOptions options,
        bool aEndsWithNewline,
        bool bEndsWithNewline) {
        var entries = BuildEntries(a, b, ops);
        List<int> changes = [];
        for (int i = 0; i < entries.Count; i++) {
            if (entries[i].Kind != LineKind.Context) changes.Add(i);
        }

        if (changes.Count == 0) return "";

        int context = Math.Max(0, options.Context);
        bool aTruncated = a.Count > 0 && !aEndsWithNewline;
        bool bTruncated = b.Count > 0 && !bEndsWithNewline;

        string headerA = options.LabelA is null ? $"--- {options.PathA}" : $"--- {options.PathA}\t{options.LabelA}";
        string headerB = options.LabelB is null ? $"+++ {options.PathB}" : $"+++ {options.PathB}\t{options.LabelB}";
        List<string> output = [headerA, headerB];

        int group = 0;
        while (group < changes.Count) {
            int first = changes[group];
            int last = first;
            int next = group + 1;
            while (next < changes.Count && changes[next] - last - 1 <= 2 * context) {
                last = changes[next];
                next++;
            }

            int start = Math.Max(0, first - context);
            int end = Math.Min(entries.Count - 1, last + context);
            AppendHunk(entries, a, b, start, end, aTruncated, bTruncated, output);
            group = next;
        }

        return string.Join("\n", output);
    }

    private static void AppendHunk(
        List<Entry> entries,
        IReadOnlyList<string> a,
        IReadOnlyList<string> b,
        int start,
        int end,
        bool aTruncated,
        bool bTruncated,
        List<string> output) {
        int aCount = 0;
        int bCount = 0;
        int aFirst = -1;
        int bFirst = -1;
        for (int i = start; i <= end; i++) {
            var entry = entries[i];
            if (entry.Kind != LineKind.Insert) {
                if (aFirst < 0) aFirst = entry.AIndex;
                aCount++;
            }

            if (entry.Kind != LineKind.Delete) {
                if (bFirst < 0) bFirst = entry.BIndex;
                bCount++;
            }
        }

        int aStart = aCount == 0 ? CountBefore(entries, start, LineKind.Insert) : aFirst + 1;
        int bStart = bCount == 0 ? CountBefore(entries, start, LineKind.Delete) : bFirst + 1;
        output.Add($"@@ -{FormatRange(aStart, aCount)} +{FormatRange(bStart, bCount)} @@");

        for (int i = start; i <= end; i++) {
            var entry = entries[i];
            if (entry.Kind == LineKind.Delete) {
                output.Add("-" + entry.Text);
                if (aTruncated && entry.AIndex == a.Count - 1) output.Add(NoNewlineMarker);
            } else if (entry.Kind == LineKind.Insert) {
                output.Add("+" + entry.Text);
                if (bTruncated && entry.BIndex == b.Count - 1) output.Add(NoNewlineMarker);
            } else {
                output.Add(" " + entry.Text);
                if ((aTruncated && entry.AIndex == a.Count - 1) || (bTruncated && entry.BIndex == b.Count - 1)) output.Add(NoNewlineMarker);
            }
        }
    }

    private static int CountBefore(List<Entry> entries, int limit, LineKind exclude) {
        int count = 0;
        for (int i = 0; i < limit; i++) {
            if (entries[i].Kind != exclude) count++;
        }

        return count;
    }

    private static string FormatRange(int start, int count) {
        string head = start.ToString(CultureInfo.InvariantCulture);
        return count == 1 ? head : head + "," + count.ToString(CultureInfo.InvariantCulture);
    }

    private static List<Entry> BuildEntries(IReadOnlyList<string> a, IReadOnlyList<string> b, IReadOnlyList<DiffOp> ops) {
        var entries = new List<Entry>(a.Count + b.Count);
        foreach (var op in ops) {
            if (op.Kind == DiffOpKind.Equal) {
                for (int i = 0; i < op.ALength; i++) entries.Add(new Entry(LineKind.Context, op.AStart + i, op.BStart + i, a[op.AStart + i]));
            } else if (op.Kind == DiffOpKind.Delete) {
                for (int i = 0; i < op.ALength; i++) entries.Add(new Entry(LineKind.Delete, op.AStart + i, -1, a[op.AStart + i]));
            } else {
                for (int i = 0; i < op.BLength; i++) entries.Add(new Entry(LineKind.Insert, -1, op.BStart + i, b[op.BStart + i]));
            }
        }

        return entries;
    }

    private enum LineKind { Context, Delete, Insert }

    private readonly record struct Entry(LineKind Kind, int AIndex, int BIndex, string Text);
}
