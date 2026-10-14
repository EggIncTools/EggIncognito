namespace EggIncognito.Core.Services.ProtoExtract;

public static class SymbolRecovery {
    private const int MinFuncLen = 32;
    private const int MaxFuncLen = 0x20000;
    private const int PrefixLen = 32;
    private const int LooseMinFuncLen = 0x100;

    public static RecoveryReport Recover(byte[] symbolizedRef, byte[] strippedTarget, string[] interestNeedles) {
        interestNeedles ??= [];
        if (symbolizedRef is null || strippedTarget is null || symbolizedRef.Length < 64 || strippedTarget.Length < 64)
            return None(interestNeedles, "ref or target binary too short");

        var refImage = new MachoImage(symbolizedRef);
        var targetImage = new MachoImage(strippedTarget);
        if (!refImage.TryFindText(out int rOff, out int rSize, out ulong rVm))
            return None(interestNeedles, "reference has no __text");
        if (!targetImage.TryFindText(out int tOff, out int tSize, out ulong tVm))
            return None(interestNeedles, "target has no __text");

        var refSyms = MachoSymbols.Read(symbolizedRef);
        if (refSyms.Count == 0) return None(interestNeedles, "reference has no symbols");

        if (rSize == tSize && rSize > 0 && SpanEqual(symbolizedRef, rOff, strippedTarget, tOff, rSize)) {
            var (found0, missing0) = Partition(refSyms, interestNeedles);
            return new RecoveryReport("exact-transplant", refSyms.Count, refSyms, found0, missing0,
                "ok: identical __text");
        }

        (var recovered, int looseCount) =
            ContentHashRecover(symbolizedRef, rOff, rVm, refSyms, strippedTarget, tOff, tSize, tVm);
        var (found, missing) = Partition(recovered, interestNeedles);
        string diag = recovered.Count == 0
            ? "no functions recovered; target is a different build"
            : $"content-hash recovered {recovered.Count} ({looseCount} loose) of {CountTextFuncs(refSyms, rVm, rSize)} reference functions";
        string tier = looseCount > 0 ? "content-hash+loose" : "content-hash";
        return new RecoveryReport(tier, recovered.Count, recovered, found, missing, diag);
    }

    private static (List<MachoSymbols.Symbol> Recovered, int LooseCount) ContentHashRecover(
        byte[] refBin, int rOff, ulong rVm, IReadOnlyList<MachoSymbols.Symbol> refSyms,
        byte[] tgtBin, int tOff, int tSize, ulong tVm) {
        ulong rSlide = rVm - (ulong)rOff;
        var ranges = FunctionRanges(refSyms, rVm, rVm + (ulong)refBin.Length);

        var byPrefix = new Dictionary<ulong, List<RefFunc>>();
        foreach ((string name, ulong start, ulong end) in ranges) {
            long fileStart = (long)start - (long)rSlide;
            long len = (long)end - (long)start;
            if (len < MinFuncLen || len > MaxFuncLen || fileStart < 0 || fileStart + len > refBin.Length) continue;
            byte[] norm = NormalizeRange(refBin, (int)fileStart, (int)len, false);
            ulong pfx = FnvPrefix(norm);
            var fn = new RefFunc(name, norm, FnvFull(norm));
            if (!byPrefix.TryGetValue(pfx, out var list)) byPrefix[pfx] = list = [];
            list.Add(fn);
        }

        if (byPrefix.Count == 0) return ([], 0);

        int tEnd = Math.Min(tOff + tSize, tgtBin.Length);
        var recovered = new List<MachoSymbols.Symbol>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var usedFullHashes = new HashSet<ulong>();

        var starts = MachoFunctionStarts.Read(tgtBin);

        IEnumerable<int> Offsets() {
            if (starts.Count > 0) {
                foreach (int s in starts) {
                    if (s >= tOff && s + PrefixLen <= tEnd)
                        yield return s;
                }
            } else {
                for (int p = tOff; p + PrefixLen <= tEnd; p += 4)
                    yield return p;
            }
        }

        foreach (int p in Offsets()) {
            ulong pfx = FnvNormalizedPrefixAt(tgtBin, p, false);
            if (!byPrefix.TryGetValue(pfx, out var cands)) continue;
            foreach (var fn in cands) {
                if (p + fn.Norm.Length > tEnd) continue;
                if (usedNames.Contains(fn.Name) || usedFullHashes.Contains(fn.FullHash)) continue;
                if (!NormalizedEquals(tgtBin, p, fn.Norm, false)) continue;
                usedNames.Add(fn.Name);
                usedFullHashes.Add(fn.FullHash);
                recovered.Add(new MachoSymbols.Symbol(fn.Name, tVm + (ulong)(p - tOff), 0, 1));
                break;
            }
        }

        int looseCount = LooseRecover(refBin, rSlide, ranges, tgtBin, tOff, tEnd, tVm, Offsets, usedNames, recovered);
        return (recovered, looseCount);
    }

    private static int LooseRecover(byte[] refBin, ulong rSlide,
        List<(string Name, ulong Start, ulong End)> ranges, byte[] tgtBin, int tOff, int tEnd, ulong tVm,
        Func<IEnumerable<int>> offsets, HashSet<string> usedNames, List<MachoSymbols.Symbol> recovered) {
        var byPrefix = new Dictionary<ulong, List<RefFunc>>();
        var byFullHash = new Dictionary<ulong, RefFunc>();
        var ambiguous = new HashSet<ulong>();
        foreach ((string name, ulong start, ulong end) in ranges) {
            if (usedNames.Contains(name)) continue;
            long fileStart = (long)start - (long)rSlide;
            long len = (long)end - (long)start;
            if (len < LooseMinFuncLen || len > MaxFuncLen || fileStart < 0 || fileStart + len > refBin.Length)
                continue;
            byte[] norm = NormalizeRange(refBin, (int)fileStart, (int)len, true);
            var fn = new RefFunc(name, norm, FnvFull(norm));
            if (!byFullHash.TryAdd(fn.FullHash, fn)) ambiguous.Add(fn.FullHash);
            ulong pfx = FnvPrefix(norm);
            if (!byPrefix.TryGetValue(pfx, out var list)) byPrefix[pfx] = list = [];
            list.Add(fn);
        }

        if (byPrefix.Count == 0) return 0;

        int looseCount = 0;
        var usedFullHashes = new HashSet<ulong>();
        foreach (int p in offsets()) {
            ulong pfx = FnvNormalizedPrefixAt(tgtBin, p, true);
            if (!byPrefix.TryGetValue(pfx, out var cands)) continue;
            foreach (var fn in cands) {
                if (ambiguous.Contains(fn.FullHash)) continue;
                if (p + fn.Norm.Length > tEnd) continue;
                if (usedNames.Contains(fn.Name) || usedFullHashes.Contains(fn.FullHash)) continue;
                if (!NormalizedEquals(tgtBin, p, fn.Norm, true)) continue;
                usedNames.Add(fn.Name);
                usedFullHashes.Add(fn.FullHash);
                recovered.Add(new MachoSymbols.Symbol(fn.Name, tVm + (ulong)(p - tOff), 0, 1));
                looseCount++;
                break;
            }
        }

        return looseCount;
    }

    private static ulong FnvFull(byte[] norm) {
        ulong h = 1469598103934665603UL;
        for (int i = 0; i < norm.Length; i++) {
            h ^= norm[i];
            h *= 1099511628211UL;
        }

        return h;
    }

    private static ulong FnvPrefix(byte[] norm) {
        ulong h = 1469598103934665603UL;
        int n = Math.Min(PrefixLen, norm.Length);
        for (int i = 0; i < n; i++) {
            h ^= norm[i];
            h *= 1099511628211UL;
        }

        return h;
    }

    private static ulong FnvNormalizedPrefixAt(byte[] bin, int off, bool loose) {
        ulong h = 1469598103934665603UL;
        for (int i = 0; i < PrefixLen; i += 4) {
            uint w = NormalizeWord((uint)(bin[off + i] | (bin[off + i + 1] << 8) | (bin[off + i + 2] << 16) |
                                          (bin[off + i + 3] << 24)), loose);
            h ^= (byte)w;
            h *= 1099511628211UL;
            h ^= (byte)(w >> 8);
            h *= 1099511628211UL;
            h ^= (byte)(w >> 16);
            h *= 1099511628211UL;
            h ^= (byte)(w >> 24);
            h *= 1099511628211UL;
        }

        return h;
    }

    private static bool NormalizedEquals(byte[] tgt, int off, byte[] norm, bool loose) {
        for (int i = 0; i + 4 <= norm.Length; i += 4) {
            uint w = NormalizeWord((uint)(tgt[off + i] | (tgt[off + i + 1] << 8) | (tgt[off + i + 2] << 16) |
                                          (tgt[off + i + 3] << 24)), loose);
            if ((byte)w != norm[i] || (byte)(w >> 8) != norm[i + 1] || (byte)(w >> 16) != norm[i + 2] ||
                (byte)(w >> 24) != norm[i + 3]) {
                return false;
            }
        }

        return true;
    }

    private static List<(string Name, ulong Start, ulong End)> FunctionRanges(
        IReadOnlyList<MachoSymbols.Symbol> syms, ulong textVm, ulong textEnd) {
        var addrs = syms.Where(s => s.Value >= textVm && s.Value < textEnd && !string.IsNullOrEmpty(s.Name))
            .Select(s => (s.Name, s.Value)).Distinct().OrderBy(s => s.Value).ToList();
        var outp = new List<(string, ulong, ulong)>(addrs.Count);
        for (int i = 0; i < addrs.Count; i++) {
            ulong start = addrs[i].Value;
            ulong end = i + 1 < addrs.Count ? addrs[i + 1].Value : textEnd;
            if (end > start) outp.Add((addrs[i].Name, start, end));
        }

        return outp;
    }

    private static int CountTextFuncs(IReadOnlyList<MachoSymbols.Symbol> syms, ulong textVm, int textSize)
        => FunctionRanges(syms, textVm, textVm + (ulong)textSize).Count;

    private static byte[] NormalizeRange(byte[] bin, int off, int len, bool loose) {
        byte[] c = new byte[len];
        Array.Copy(bin, off, c, 0, len);
        for (int i = 0; i + 4 <= len; i += 4) {
            uint w = (uint)(c[i] | (c[i + 1] << 8) | (c[i + 2] << 16) | (c[i + 3] << 24));
            uint nw = NormalizeWord(w, loose);
            c[i] = (byte)nw;
            c[i + 1] = (byte)(nw >> 8);
            c[i + 2] = (byte)(nw >> 16);
            c[i + 3] = (byte)(nw >> 24);
        }

        return c;
    }

    private static uint NormalizeWord(uint w, bool loose) {
        uint top6 = w >> 26;
        uint top8 = w >> 24;
        bool brImm = top6 is 0b000101 or 0b100101;
        bool bcond = top8 == 0b01010100;
        bool adr = (w & 0x1F000000) == 0x10000000;
        if (adr) return w & 0x9F000000;
        if (brImm || bcond) return w & 0xFF000000;
        if (!loose) return w;
        if (top8 is 0x11 or 0x91) return w & ~0x003FFC00u;
        uint op = w & 0xFFC00000;
        if (op is 0xB9400000 or 0xF9400000 or 0xBD400000 or 0xFD400000 or 0x3DC00000) return w & ~0x003FFC00u;
        if (op is 0x29400000 or 0xA9400000 or 0x2D400000 or 0x6D400000 or 0xAD400000) return w & ~0x003F8000u;
        return w;
    }

    private static (IReadOnlyList<string> Found, IReadOnlyList<string> Missing) Partition(
        IReadOnlyList<MachoSymbols.Symbol> syms, string[] needles) {
        var found = new List<string>();
        var missing = new List<string>();
        foreach (string n in needles) {
            if (syms.Any(s => s.Name.Contains(n, StringComparison.Ordinal))) found.Add(n);
            else missing.Add(n);
        }

        return (found, missing);
    }

    private static RecoveryReport None(string[] needles, string diag)
        => new("none", 0, [], [], needles, diag);

    private static bool SpanEqual(byte[] a, int aOff, byte[] b, int bOff, int len) => aOff >= 0 && bOff >= 0 &&
        aOff + len <= a.Length && bOff + len <= b.Length && a.AsSpan(aOff, len).SequenceEqual(b.AsSpan(bOff, len));

    public readonly record struct RecoveryReport(
        string Tier,
        int Recovered,
        IReadOnlyList<MachoSymbols.Symbol> Symbols,
        IReadOnlyList<string> RequestedFound,
        IReadOnlyList<string> RequestedMissing,
        string Diagnostics);

    private readonly record struct RefFunc(string Name, byte[] Norm, ulong FullHash);
}
