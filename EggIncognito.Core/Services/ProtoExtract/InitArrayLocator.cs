using System.Runtime.CompilerServices;

namespace EggIncognito.Core.Services.ProtoExtract;

public sealed class InitArrayLocator {
    private static readonly ConditionalWeakTable<byte[], InitArrayLocator> Cache = [];

    private readonly byte[] _bin;
    private readonly IBinaryImage _img;
    private readonly List<ulong> _inits;
    private readonly Lazy<IReadOnlyList<Init>> _scanned;

    private InitArrayLocator(byte[] bin, IBinaryImage img) {
        _bin = bin;
        _img = img;
        _inits = [.. img.GetInitArrayTargets().Where(t => t != 0).Distinct()];
        _inits.Sort();
        _scanned = new Lazy<IReadOnlyList<Init>>(ScanAll);
    }

    public static InitArrayLocator? Create(byte[] bin) {
        if (Cache.TryGetValue(bin, out var cached)) return cached;
        var img = BinaryImage.Load(bin);
        if (img is null) return null;
        var made = new InitArrayLocator(bin, img);
        Cache.AddOrUpdate(bin, made);
        return made;
    }

    public IReadOnlyList<ulong> Inits => _inits;

    public bool TryLocateByString(string needle, out ulong start, out ulong end) {
        foreach ((ulong s, ulong e) in LocateAllByString(needle)) {
            start = s;
            end = e;
            return true;
        }

        start = 0;
        end = 0;
        return false;
    }

    public (ulong Start, ulong End)? RangeOf(ulong initVa) {
        int idx = _inits.IndexOf(initVa);
        if (idx < 0) return null;
        ulong cap = idx + 1 < _inits.Count ? _inits[idx + 1] : EndOf(initVa);
        return (initVa, FunctionEnd(initVa, cap));
    }

    public IEnumerable<(ulong Start, ulong End)> LocateAllByString(string needle) {
        foreach (var init in _scanned.Value) {
            if (init.Strings.Any(s => s.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                yield return (init.Start, init.End);
        }
    }

    private List<Init> ScanAll() {
        var outp = new List<Init>(_inits.Count);
        for (int i = 0; i < _inits.Count; i++) {
            ulong s = _inits[i];
            ulong cap = i + 1 < _inits.Count ? _inits[i + 1] : EndOf(s);
            ulong fnEnd = FunctionEnd(s, cap);
            outp.Add(new Init(s, fnEnd, StringsIn(s, fnEnd)));
        }

        return outp;
    }

    private ulong FunctionEnd(ulong start, ulong cap) {
        var lst = Arm64DataTableReader.ListRange(_bin, start, cap, 200_000);
        if (!lst.Ok) return cap;
        foreach (var insn in lst.Instructions) {
            if (insn.Mnemonic.StartsWith("ret", StringComparison.Ordinal)) return insn.Va + 4;
        }

        return cap;
    }

    private string[] StringsIn(ulong startVa, ulong endVa) {
        var scan = Arm64DataTableReader.ScanRange(_bin, startVa, endVa);
        if (!scan.Ok) return [];
        var strings = new List<string>();
        foreach (var r in scan.Addresses) {
            if (!BinaryStrings.IsStringSection(r.Section)) continue;
            string s = BinaryStrings.ReadCstr(_bin, _img, r.Va, 200);
            if (s.Length > 0) strings.Add(s);
        }

        return [.. strings];
    }

    private ulong EndOf(ulong start) {
        var index = CodeIndex.Build(_img);
        return index.TryResolve(start, out var range, out _) ? range.End : start;
    }

    private sealed record Init(ulong Start, ulong End, string[] Strings);
}
