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
        var tracker = new Arm64PageTracker { Image = img is null ? null : new Arm64Image(bin, img) };
        var seen = new HashSet<long>();
        var outp = new List<double>();

        void Emit(double d) {
            if (!IsValue(d)) return;
            long key = BitConverter.DoubleToInt64Bits(d);
            if (seen.Add(key)) outp.Add(d);
        }

        bool TryWord(ulong va, out uint word) {
            word = 0;
            if (img is null || !img.TryVaToFileOffset(va, out int fo, out _) || fo + 4 > bin.Length) return false;
            word = BitConverter.ToUInt32(bin.AsSpan(fo));
            return true;
        }

        for (int i = 0; i < insns.Count; i++) {
            var insn = insns[i];
            string m = insn.Mnemonic;
            string ops = insn.Operands;

            if (!TryWord(insn.Va, out uint word)) continue;
            tracker.Track(insn.Va, word);

            bool isStart = m == "movz" || (m == "orr" && IsOrrMoveImm(ops));
            if (isStart && TryReg(ops, out string reg) && TryRegIndex(reg, out int idx)) {
                int j = i + 1;
                while (j < insns.Count && insns[j].Mnemonic == "movk"
                                       && TryReg(insns[j].Operands, out string reg2) && reg2 == reg) {
                    if (TryWord(insns[j].Va, out uint mkWord)) tracker.Track(insns[j].Va, mkWord);
                    j++;
                }

                if (tracker.TryValue(idx, out ulong bits)) Emit(Arm64Bits.F64(bits));
                i = j - 1;
                continue;
            }

            if ((m == "ldr" || m == "ldur") && TryReg(ops, out string dreg)) {
                bool isWide = dreg.StartsWith("q", StringComparison.OrdinalIgnoreCase);
                bool isDouble = isWide || dreg.StartsWith("d", StringComparison.OrdinalIgnoreCase);
                if (!isDouble) continue;

                if (!isWide && TryRegIndex(dreg, out int fidx) && tracker.TryF64(fidx, out double fval)) {
                    Emit(fval);
                    continue;
                }

                if (TryWideLoadAddress(ops, tracker, out ulong lva) && img is not null
                                                                     && img.TryVaToFileOffset(lva, out int lfo, out _)) {
                    if (isWide && lfo + 16 <= bin.Length) {
                        Emit(BitConverter.ToDouble(bin, lfo));
                        Emit(BitConverter.ToDouble(bin, lfo + 8));
                    } else if (!isWide && lfo + 8 <= bin.Length) {
                        Emit(BitConverter.ToDouble(bin, lfo));
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

    private static bool TryRegIndex(string reg, out int idx) {
        idx = 0;
        return reg.Length >= 2
            && int.TryParse(reg[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out idx);
    }

    private static bool IsOrrMoveImm(string ops) {
        string[] p = ops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return p.Length >= 3 && p[1] is "xzr" or "wzr";
    }

    private static bool TryWideLoadAddress(string ops, Arm64PageTracker tracker, out ulong va) {
        va = 0;
        string[] p = ops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2) return false;

        string mem = p[1];
        int open = mem.IndexOf('[');
        if (open < 0) return false;
        string body = mem[(open + 1)..].TrimEnd(']', '!', ' ');
        string[] mp = body.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (mp.Length == 0 || !TryRegIndex(mp[0], out int baseIdx) || !tracker.TryValue(baseIdx, out ulong baseVal))
            return false;
        long disp = 0;
        if (mp.Length >= 2 && mp[1].StartsWith('#')) ParseImm(mp[1], out disp);
        va = baseVal + (ulong)disp;
        return true;
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
