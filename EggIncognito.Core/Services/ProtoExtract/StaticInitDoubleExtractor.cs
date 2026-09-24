using System.Globalization;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class StaticInitDoubleExtractor {
    public static Result Extract(byte[] bin, string symbol, int maxInsns = 6000)
        => ExtractWith(bin, BinaryImage.Load(bin)?.Symbols ?? [], symbol, maxInsns);

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, string symbol,
        int maxInsns = 6000) {
        var lst = Arm64DataTableReader.ListWith(bin, syms, [symbol], maxInsns);
        return !lst.Ok ? new Result(false, [], symbol, lst.Diagnostics) : Decode(bin, lst.Instructions, symbol);
    }

    public static Result ExtractRange(byte[] bin, ulong startVa, ulong endVa, int maxInsns = 60000) {
        var lst = Arm64DataTableReader.ListRange(bin, startVa, endVa, maxInsns);
        return !lst.Ok ? new Result(false, [], "range", lst.Diagnostics) : Decode(bin, lst.Instructions, "range");
    }

    private static Result Decode(byte[] bin, IReadOnlyList<Arm64DataTableReader.Insn> insns, string symbol) {
        var img = BinaryImage.Load(bin);
        var seen = new HashSet<long>();
        var outp = new List<double>();
        var page = new Dictionary<string, ulong>();

        void Emit(long bits) {
            double d = BitConverter.Int64BitsToDouble(bits);
            if (!IsValue(d)) return;
            long key = BitConverter.DoubleToInt64Bits(d);
            if (seen.Add(key)) outp.Add(d);
        }

        for (int i = 0; i < insns.Count; i++) {
            string m = insns[i].Mnemonic;
            string ops = insns[i].Operands;

            if (m == "adrp" && TryReg(ops, out string ar) && TryLastImm(ops, out long apage)) {
                page[ar] = (ulong)apage;
                continue;
            }

            if (m == "add" && TryReg(ops, out string dr) && TryAddBase(ops, page, out ulong full)) {
                page[dr] = full;
                continue;
            }

            if ((m == "movz" || m == "orr") && TryComposeStart(m, ops, out string reg, out long acc)) {
                long bits = acc;
                int j = i + 1;
                while (j < insns.Count && insns[j].Mnemonic == "movk"
                                       && TryMovk(insns[j].Operands, reg, out long part, out int laneShift)) {
                    long lane = 0xFFFFL << laneShift;
                    bits = (bits & ~lane) | part;
                    j++;
                }

                Emit(bits);
                continue;
            }

            if ((m == "ldr" || m == "ldur") && TryMemLoad(ops, page, out ulong va, out bool isWide)) {
                if (img is not null && img.TryVaToFileOffset(va, out int fo, out _)) {
                    if (isWide && fo + 16 <= bin.Length) {
                        Emit(BitConverter.ToInt64(bin.AsSpan(fo)));
                        Emit(BitConverter.ToInt64(bin.AsSpan(fo + 8)));
                    } else if (!isWide && fo + 8 <= bin.Length) {
                        Emit(BitConverter.ToInt64(bin.AsSpan(fo)));
                    }
                }
            }
        }

        return new Result(true, outp, symbol, "ok");
    }

    private static bool IsValue(double d)
        => double.IsFinite(d) && Math.Abs(d) >= 1e-6 && Math.Abs(d) <= 1e12;

    private static bool TryReg(string ops, out string reg) {
        reg = "";
        string[] p = ops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0) return false;
        reg = p[0];
        return reg.Length > 0;
    }

    private static bool TryLastImm(string ops, out long imm) {
        imm = 0;
        string[] p = ops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (int k = p.Length - 1; k >= 0; k--) {
            if (p[k].StartsWith('#'))
                return ParseImm(p[k], out imm);
        }

        return false;
    }

    private static bool TryAddBase(string ops, Dictionary<string, ulong> page, out ulong full) {
        full = 0;
        string[] p = ops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 3) return false;
        if (!page.TryGetValue(p[1], out ulong basePage)) return false;
        if (!p[2].StartsWith('#') || !ParseImm(p[2], out long off)) return false;
        full = basePage + (ulong)off;
        return true;
    }

    private static bool TryComposeStart(string mnemonic, string ops, out string reg, out long acc) {
        reg = "";
        acc = 0;
        string[] p = ops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2) return false;
        reg = p[0];

        if (mnemonic == "orr") {
            if (p.Length < 3) return false;
            if (p[1] is not ("xzr" or "wzr")) return false;
            if (!p[2].StartsWith('#') || !ParseImm(p[2], out long mask)) return false;
            acc = mask;
            return true;
        }

        if (!p[1].StartsWith('#') || !ParseImm(p[1], out long imm)) return false;
        int shift = 0;
        if (p.Length >= 3 && p[2].StartsWith("lsl", StringComparison.OrdinalIgnoreCase))
            shift = ParseShift(p[2]);
        acc = imm << shift;
        return true;
    }

    private static bool TryMovk(string ops, string reg, out long part, out int shift) {
        part = 0;
        shift = 0;
        string[] p = ops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2 || p[0] != reg) return false;
        if (!p[1].StartsWith('#') || !ParseImm(p[1], out long imm)) return false;
        if (p.Length >= 3 && p[2].StartsWith("lsl", StringComparison.OrdinalIgnoreCase))
            shift = ParseShift(p[2]);
        part = (imm & 0xFFFF) << shift;
        return true;
    }

    private static bool TryMemLoad(string ops, Dictionary<string, ulong> page, out ulong va, out bool isWide) {
        va = 0;
        isWide = false;
        string[] p = ops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2) return false;
        isWide = p[0].StartsWith("q", StringComparison.OrdinalIgnoreCase);
        if (!isWide && !p[0].StartsWith("d", StringComparison.OrdinalIgnoreCase)) return false;

        string mem = p[1];
        int open = mem.IndexOf('[');
        if (open < 0) return false;
        string body = mem[(open + 1)..].TrimEnd(']', '!', ' ');
        string[] mp = body.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (mp.Length == 0 || !page.TryGetValue(mp[0], out ulong basePage)) return false;
        long disp = 0;
        if (mp.Length >= 2 && mp[1].StartsWith('#')) ParseImm(mp[1], out disp);
        va = basePage + (ulong)disp;
        return true;
    }

    private static int ParseShift(string tok) {
        string[] sh = tok.Split('#', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return sh.Length == 2 && int.TryParse(sh[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)
            ? s
            : 0;
    }

    private static bool ParseImm(string tok, out long imm) {
        imm = 0;
        string t = tok.StartsWith('#') ? tok[1..] : tok;
        bool neg = false;
        if (t.StartsWith('-')) {
            neg = true;
            t = t[1..];
        }

        bool ok = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong u)
            : ulong.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out u);
        if (!ok) return false;
        imm = neg ? -(long)u : unchecked((long)u);
        return true;
    }

    public readonly record struct Result(bool Ok, IReadOnlyList<double> Values, string Symbol, string Diagnostics);
}
