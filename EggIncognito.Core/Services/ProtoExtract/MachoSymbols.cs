using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class MachoSymbols {
    private const uint MhMagic64 = 0xFEEDFACF;
    private const uint FatMagic = 0xCAFEBABE;
    private const uint FatMagicLe = 0xBEBAFECA;
    private const uint CpuArm64 = 0x0100000C;
    private const uint LcSymtab = 0x02;

    public static IReadOnlyList<Symbol> Read(byte[] bin) {
        var outp = new List<Symbol>();
        if (bin is null || bin.Length < 32) return outp;
        try {
            uint magic = U32(bin, 0);
            int b = 0;
            if (magic is FatMagic or FatMagicLe) {
                if (!TryFatArm64(bin, out b) || b + 32 > bin.Length) return outp;
                magic = U32(bin, b);
            }

            if (magic != MhMagic64) return outp;

            uint ncmds = U32(bin, b + 16);
            int lc = b + 32;
            for (uint c = 0; c < ncmds; c++) {
                if (lc + 8 > bin.Length) return outp;
                uint cmd = U32(bin, lc);
                uint cmdsize = U32(bin, lc + 4);
                if (cmdsize < 8 || lc + cmdsize > bin.Length) return outp;
                if (cmd == LcSymtab) {
                    uint symoff = U32(bin, lc + 8) + (uint)b;
                    uint nsyms = U32(bin, lc + 12);
                    uint stroff = U32(bin, lc + 16) + (uint)b;
                    uint strsize = U32(bin, lc + 20);
                    ReadNlist(bin, symoff, nsyms, stroff, strsize, outp);
                }

                lc += (int)cmdsize;
            }
        } catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException
                                         or OverflowException) {
            return outp;
        }

        return outp;
    }

    public static bool TryFindFunc(IReadOnlyList<Symbol> syms, string[] needles, out FuncRange range) {
        range = default;
        if (syms is null || needles is null || needles.Length == 0) return false;

        bool wantLocal = needles.Any(IsLocalEntity);
        Symbol? hit = null;
        int bestRank = int.MinValue;

        foreach (var s in syms) {
            if (s.Value == 0 || string.IsNullOrEmpty(s.Name)) continue;
            if (!MatchesAll(s.Name, needles)) continue;

            int rank = Rank(s.Name, needles, wantLocal);
            if (hit is null || rank > bestRank || (rank == bestRank && IsBetterTieBreak(s, hit.Value))) {
                hit = s;
                bestRank = rank;
            }
        }

        if (hit is null) return false;
        range = new FuncRange(hit.Value.Name, hit.Value.Value, EndOf(syms, hit.Value.Value));
        return true;
    }

    public static bool TryResolveVa(IReadOnlyList<Symbol> syms, ulong va, out FuncRange range, out ulong offset)
        => Index.Build(syms).TryResolve(va, out range, out offset);

    private static bool MatchesAll(string name, string[] needles) {
        foreach (string n in needles) {
            if (!name.Contains(n, StringComparison.Ordinal)) return false;
        }

        return true;
    }

    private static int Rank(string name, string[] needles, bool wantLocal) {
        foreach (string n in needles) {
            if (string.Equals(name, n, StringComparison.Ordinal)) return 2;
            if (string.Equals(name, "_" + n, StringComparison.Ordinal)) return 2;
        }

        return wantLocal || !IsLocalEntity(name) ? 1 : 0;
    }

    private static bool IsBetterTieBreak(Symbol candidate, Symbol current) {
        if (candidate.Name.Length != current.Name.Length) return candidate.Name.Length < current.Name.Length;
        return candidate.Value < current.Value;
    }

    public static bool IsLocalEntity(string name) {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Contains("$_", StringComparison.Ordinal)) return true;
        if (name.Contains("_block_invoke", StringComparison.Ordinal)) return true;
        string t = name.Length > 1 && name[0] == '_' ? name[1..] : name;
        return t.StartsWith("_ZZ", StringComparison.Ordinal);
    }

    private static ulong EndOf(IReadOnlyList<Symbol> syms, ulong start) {
        ulong end = ulong.MaxValue;
        foreach (var s in syms) {
            if (s.Value > start && s.Value < end)
                end = s.Value;
        }

        return end == ulong.MaxValue ? start + 0x4000 : end;
    }

    private static void ReadNlist(byte[] bin, uint symoff, uint nsyms, uint stroff, uint strsize, List<Symbol> outp) {
        for (uint i = 0; i < nsyms; i++) {
            long e = symoff + (long)i * 16;
            if (e + 16 > bin.Length) return;
            uint nStrx = U32(bin, (int)e);
            byte nType = bin[e + 4];
            byte nSect = bin[e + 5];
            ulong nValue = U64(bin, (int)e + 8);
            if (nStrx == 0 || nStrx >= strsize) continue;
            string name = Cstr(bin, (int)(stroff + nStrx));
            if (name.Length == 0) continue;
            outp.Add(new Symbol(name, nValue, nType, nSect));
        }
    }

    private static bool TryFatArm64(byte[] b, out int offset) {
        offset = 0;
        if (b.Length < 8) return false;
        uint nfat = U32be(b, 4);
        int e = 8;
        for (uint i = 0; i < nfat; i++) {
            if (e + 20 > b.Length) return false;
            if (U32be(b, e) == CpuArm64) {
                offset = (int)U32be(b, e + 8);
                return true;
            }

            e += 20;
        }

        return false;
    }

    private static uint U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    private static uint U32be(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

    private static ulong U64(byte[] b, int o) {
        ulong v = 0;
        for (int k = 0; k < 8; k++) v |= (ulong)b[o + k] << (k * 8);
        return v;
    }

    private static string Cstr(byte[] b, int o) {
        if (o < 0 || o >= b.Length) return "";
        int end = o;
        while (end < b.Length && b[end] != 0) end++;
        return Encoding.UTF8.GetString(b, o, end - o);
    }

    public readonly record struct Symbol(string Name, ulong Value, byte Type, byte Sect);

    public readonly record struct FuncRange(string Name, ulong Start, ulong End);

    public sealed class Index(ulong[] starts, string[] names) {
        public static Index Build(IReadOnlyList<Symbol> syms) {
            var best = new Dictionary<ulong, string>();
            foreach (var s in syms) {
                if (s.Value == 0 || string.IsNullOrEmpty(s.Name)) continue;
                if (!best.TryGetValue(s.Value, out string? cur) || PrefersOver(s.Name, cur))
                    best[s.Value] = s.Name;
            }

            ulong[] starts = [.. best.Keys.OrderBy(v => v)];
            string[] names = new string[starts.Length];
            for (int i = 0; i < starts.Length; i++) names[i] = best[starts[i]];
            return new Index(starts, names);
        }

        private static bool PrefersOver(string candidate, string current) {
            bool cl = IsLocalEntity(candidate);
            bool ul = IsLocalEntity(current);
            if (cl != ul) return !cl;
            return candidate.Length < current.Length;
        }

        public bool TryResolve(ulong va, out FuncRange range, out ulong offset) {
            range = default;
            offset = 0;
            int lo = 0;
            int hi = starts.Length;
            while (lo < hi) {
                int mid = (lo + hi) / 2;
                if (starts[mid] <= va) lo = mid + 1;
                else hi = mid;
            }

            if (lo == 0) return false;
            int i = lo - 1;
            ulong end = i + 1 < starts.Length ? starts[i + 1] : starts[i] + 0x4000;
            range = new FuncRange(names[i], starts[i], end);
            offset = va - starts[i];
            return true;
        }

        public string NameOf(ulong va) => TryResolve(va, out var r, out _) ? r.Name : "";
    }
}
