using System.Globalization;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class Arm64Operands {
    public static List<string> SplitOps(string operands) {
        var outp = new List<string>();
        int depth = 0;
        int start = 0;
        for (int k = 0; k < operands.Length; k++) {
            char c = operands[k];
            if (c == '[') {
                depth++;
            } else if (c == ']') {
                depth--;
            } else if (c == ',' && depth == 0) {
                outp.Add(operands[start..k].Trim());
                start = k + 1;
            }
        }

        if (start < operands.Length) outp.Add(operands[start..].Trim());
        return outp;
    }

    public static bool TryImm(string token, out ulong value) {
        value = 0;
        string t = token.Trim();
        if (t.StartsWith('#')) t = t[1..];
        if (t.EndsWith('!')) t = t[..^1];
        t = t.Trim();
        bool neg = t.StartsWith('-');
        if (neg) t = t[1..];
        bool ok = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : ulong.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        if (ok && neg) value = (ulong)-(long)value;
        return ok;
    }

    public static bool TryImm(string token, out long value) {
        bool ok = TryImm(token, out ulong raw);
        value = unchecked((long)raw);
        return ok;
    }

    public static bool TryFpImm(string token, out double value) {
        value = 0;
        string t = token.Trim();
        if (t.StartsWith('#')) t = t[1..];
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryMem(string token, out string baseReg, out ulong offset)
        => TryMem(token, out baseReg, out offset, out _, out _);

    public static bool TryMem(string token, out string baseReg, out ulong offset, out string? indexReg,
        out int indexShift) {
        baseReg = "";
        offset = 0;
        indexReg = null;
        indexShift = 0;
        string t = token.Trim();
        int lb = t.IndexOf('[');
        int rb = t.IndexOf(']');
        if (lb < 0 || rb < 0 || rb < lb) return false;
        var parts = SplitOps(t[(lb + 1)..rb]);
        if (parts.Count == 0) return false;
        baseReg = parts[0].Trim();
        if (parts.Count >= 2) {
            string second = parts[1].Trim();
            if (LooksLikeGpr(second)) {
                indexReg = second;
                if (parts.Count >= 3) indexShift = ShiftOf([parts[2]]);
            } else {
                TryImm(second, out offset);
            }
        }

        return true;
    }

    public static int ShiftOf(IReadOnlyList<string> ops) {
        foreach (string o in ops) {
            string t = o.Trim();
            if (t.StartsWith("lsl", StringComparison.OrdinalIgnoreCase) &&
                TryImm(t[3..].TrimStart('#', ' '), out ulong s)) {
                return (int)s;
            }
        }

        return 0;
    }

    public static string RegNum(string reg) {
        string t = reg.Trim();
        return t.Length > 1 && (t[0] == 'w' || t[0] == 'x') ? t[1..] : t;
    }

    public static bool IsWriteback(string memToken) => memToken.TrimEnd().EndsWith('!');

    public static bool LooksLikeGpr(string tok) =>
        tok.Length >= 2 && (tok[0] == 'x' || tok[0] == 'w') && char.IsDigit(tok[1]);

    public static bool IsZeroReg(string tok) => tok is "wzr" or "xzr";

    public static bool LooksLikeVecReg(string tok) =>
        tok.Length >= 2 && tok[0] is 'q' or 'v' or 'd' or 's' && char.IsDigit(tok[1]);

    public static int VecWidth(string tok) =>
        tok.Length >= 1 ? tok[0] switch { 'q' or 'v' => 16, 'd' => 8, 's' => 4, _ => 16 } : 16;
}
