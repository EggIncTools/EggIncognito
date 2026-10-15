using System.Runtime.CompilerServices;

namespace EggIncognito.Core.Services.ProtoExtract;

public sealed class CodeIndex {
    private const int DesymbolizedThreshold = 500;
    private const ulong FallbackSpan = 0x4000;

#pragma warning disable IDE0028
    private static readonly ConditionalWeakTable<IBinaryImage, CodeIndex> Cache = new();
#pragma warning restore IDE0028

    private readonly ulong[] _starts;
    private readonly ulong[] _ends;
    private readonly string[] _names;

    private CodeIndex(ulong[] starts, ulong[] ends, string[] names, IReadOnlyList<MachoSymbols.Symbol> symbols,
        bool desymbolized, int namedFunctionCount, int functionStartCount) {
        _starts = starts;
        _ends = ends;
        _names = names;
        Symbols = symbols;
        Desymbolized = desymbolized;
        NamedFunctionCount = namedFunctionCount;
        FunctionStartCount = functionStartCount;
    }

    public bool Desymbolized { get; }
    public int NamedFunctionCount { get; }
    public int FunctionStartCount { get; }
    public IReadOnlyList<MachoSymbols.Symbol> Symbols { get; }

    public static CodeIndex Build(IBinaryImage img) {
        if (Cache.TryGetValue(img, out var cached)) return cached;
        var built = BuildCore(img);
        Cache.AddOrUpdate(img, built);
        return built;
    }

    private static CodeIndex BuildCore(IBinaryImage img) {
        var raw = img.Symbols;
        bool isElf = img is ElfImage;
        bool haveText = img.TryFindText(out int textFo, out int textSize, out ulong textVm);
        ulong textStart = haveText ? textVm : 0;
        ulong textEnd = haveText ? textVm + (ulong)textSize : 0;

        int namedInText = 0;
        foreach (var s in raw) {
            if (s.Kind == SymbolKind.Function && s.Value != 0 && !string.IsNullOrEmpty(s.Name)
                && (!haveText || (s.Value >= textStart && s.Value < textEnd))) {
                namedInText++;
            }
        }

        bool desymbolized = namedInText < DesymbolizedThreshold;

        int functionStartCount = 0;
        List<(ulong Va, string Name)>? synthetic = null;
        if (!isElf) {
            var starts = MachoFunctionStarts.Read(img.Bytes);
            functionStartCount = starts.Count;
            if (haveText && desymbolized && starts.Count > 0) {
                synthetic = [];
                foreach (int fo in starts) {
                    ulong va = textVm + (ulong)(fo - textFo);
                    synthetic.Add((va, "sub_" + va.ToString("x")));
                }
            }
        }

        var bestAt = new Dictionary<ulong, MachoSymbols.Symbol>();
        foreach (var s in raw) {
            if (s.Value == 0 || string.IsNullOrEmpty(s.Name)) continue;
            if (!bestAt.TryGetValue(s.Value, out var cur) || PrefersOver(s, cur)) bestAt[s.Value] = s;
        }

        if (synthetic is not null) {
            foreach (var (va, name) in synthetic) {
                if (va == 0 || bestAt.ContainsKey(va)) continue;
                bestAt[va] = new MachoSymbols.Symbol(name, va, 0, 0, 0, SymbolKind.Function);
            }
        }

        ulong[] starts2 = [.. bestAt.Keys.OrderBy(v => v)];
        var ends2 = new ulong[starts2.Length];
        var names2 = new string[starts2.Length];

        for (int i = 0; i < starts2.Length; i++) {
            var sym = bestAt[starts2[i]];
            names2[i] = sym.Name;
            ends2[i] = ComputeEnd(img, sym, i, starts2);
        }

        return new CodeIndex(starts2, ends2, names2, raw, desymbolized, namedInText, functionStartCount);
    }

    private static ulong ComputeEnd(IBinaryImage img, MachoSymbols.Symbol sym, int i, ulong[] starts) {
        if (sym.Size != 0) return sym.Value + sym.Size;
        if (sym.Kind == SymbolKind.Object) return sym.Value;

        ulong sectionEnd = img.SectionAt(sym.Value) is { VmSize: not 0 } sec
            ? sec.VmAddr + sec.VmSize
            : ulong.MaxValue;
        ulong next = i + 1 < starts.Length ? starts[i + 1] : ulong.MaxValue;
        ulong capped = Math.Min(next, sectionEnd);
        return capped == ulong.MaxValue ? sym.Value + FallbackSpan : capped;
    }

    private static bool PrefersOver(MachoSymbols.Symbol candidate, MachoSymbols.Symbol current) {
        bool cl = MachoSymbols.IsLocalEntity(candidate.Name);
        bool ul = MachoSymbols.IsLocalEntity(current.Name);
        if (cl != ul) return !cl;
        return candidate.Name.Length < current.Name.Length;
    }

    public bool TryResolve(ulong va, out MachoSymbols.FuncRange range, out ulong offset) {
        range = default;
        offset = 0;
        if (!TryNearestPreceding(va, out var near, out ulong off)) return false;
        if (va >= near.End) return false;
        range = near;
        offset = off;
        return true;
    }

    public bool TryNearestPreceding(ulong va, out MachoSymbols.FuncRange range, out ulong offset) {
        range = default;
        offset = 0;
        int lo = 0;
        int hi = _starts.Length;
        while (lo < hi) {
            int mid = (lo + hi) / 2;
            if (_starts[mid] <= va) lo = mid + 1;
            else hi = mid;
        }

        if (lo == 0) return false;
        int i = lo - 1;
        range = new MachoSymbols.FuncRange(_names[i], _starts[i], _ends[i]);
        offset = va - _starts[i];
        return true;
    }

    public string NameOf(ulong va) => TryResolve(va, out var range, out _) ? range.Name : "";

    public string Display(string name) => CppDemangler.Demangle(name) ?? name;

    public bool TryFindFunc(string[] needles, out MachoSymbols.FuncRange range) {
        range = default;
        if (needles is null || needles.Length == 0) return false;

        bool wantLocal = needles.Any(MachoSymbols.IsLocalEntity);
        MachoSymbols.Symbol? hit = null;
        int bestRank = int.MinValue;

        foreach (var s in Symbols) {
            if (s.Value == 0 || string.IsNullOrEmpty(s.Name)) continue;
            string? demangled = CppDemangler.Demangle(s.Name);
            if (!MatchesAll(s.Name, demangled, needles)) continue;

            int rank = Rank(s.Name, demangled, needles, wantLocal);
            if (hit is null || rank > bestRank || (rank == bestRank && IsBetterTieBreak(s, hit.Value))) {
                hit = s;
                bestRank = rank;
            }
        }

        if (hit is null) return false;
        ulong end = hit.Value.Size != 0 ? hit.Value.Value + hit.Value.Size : FallbackEnd(hit.Value.Value);
        range = new MachoSymbols.FuncRange(hit.Value.Name, hit.Value.Value, end);
        return true;
    }

    private ulong FallbackEnd(ulong start) {
        int lo = 0;
        int hi = _starts.Length;
        while (lo < hi) {
            int mid = (lo + hi) / 2;
            if (_starts[mid] <= start) lo = mid + 1;
            else hi = mid;
        }

        return lo > 0 && _starts[lo - 1] == start ? _ends[lo - 1] : start + FallbackSpan;
    }

    private static bool MatchesAll(string name, string? demangled, string[] needles) {
        foreach (string n in needles) {
            bool inName = name.Contains(n, StringComparison.Ordinal);
            bool inDemangled = demangled is not null && demangled.Contains(n, StringComparison.Ordinal);
            if (!inName && !inDemangled) return false;
        }

        return true;
    }

    private static int Rank(string name, string? demangled, string[] needles, bool wantLocal) {
        foreach (string n in needles) {
            if (string.Equals(name, n, StringComparison.Ordinal)) return 2;
            if (string.Equals(name, "_" + n, StringComparison.Ordinal)) return 2;
            if (demangled is not null && string.Equals(demangled, n, StringComparison.Ordinal)) return 2;
        }

        return wantLocal || !MachoSymbols.IsLocalEntity(name) ? 1 : 0;
    }

    private static bool IsBetterTieBreak(MachoSymbols.Symbol candidate, MachoSymbols.Symbol current) {
        if (candidate.Name.Length != current.Name.Length) return candidate.Name.Length < current.Name.Length;
        return candidate.Value < current.Value;
    }

    public IReadOnlyList<MachoSymbols.Symbol> Find(string needle) {
        var outp = new List<MachoSymbols.Symbol>();
        if (string.IsNullOrEmpty(needle)) return outp;
        foreach (var s in Symbols) {
            if (string.IsNullOrEmpty(s.Name)) continue;
            if (s.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)) {
                outp.Add(s);
                continue;
            }

            string? demangled = CppDemangler.Demangle(s.Name);
            if (demangled is not null && demangled.Contains(needle, StringComparison.OrdinalIgnoreCase))
                outp.Add(s);
        }

        return outp;
    }
}
