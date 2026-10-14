namespace EggIncognito.Core.Services.ProtoExtract;

public sealed record ProtoDiffBundle(
    ProtoDiffResult Structural,
    IReadOnlyList<DiffOp> Ops,
    string Unified,
    SideBySideResult Split,
    bool TooLarge);

public static class ProtoDiffService {
    public static ProtoDiffBundle Build(string oldText, string newText, int maxBytes = 4_000_000,
        UnifiedDiffOptions? unifiedOptions = null) {
        if (oldText.Length > maxBytes || newText.Length > maxBytes) {
            return new ProtoDiffBundle(new ProtoDiffResult([]), [], "", new SideBySideResult([], []), true);
        }

        string[] a = UnifiedDiffWriter.SplitLines(oldText);
        string[] b = UnifiedDiffWriter.SplitLines(newText);
        var ops = MyersDiff.Compute<string>(a, b);
        string unified = UnifiedDiffWriter.Write(a, b, ops, unifiedOptions ?? new UnifiedDiffOptions());
        var split = SideBySideDiffBuilder.Build(a, b, ops);
        var structural = ProtoDiff.Compute(oldText, newText);
        return new ProtoDiffBundle(structural, ops, unified, split, false);
    }
}
