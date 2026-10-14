using System.Globalization;
using System.Text;
using static EggIncognito.Core.Services.ProtoExtract.Arm64Operands;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class Arm64StringTracer {
    private const int MaxLiteral = 160;
    private const ulong Hundred = 0x4059000000000000;

    private static readonly (string Needle, string Token, int ArgReg)[] KnownCalls = [
        ("GameDimensions19value_modify_prefix", "prefix", 0),
        ("GameDimensions12format_value", "value", 0),
        ("GameDimensions8name_str", "label", 1),
        ("GameController12currencyName", "currency", -1),
        ("_Z12comma_number", "comma", -1),
        ("3loc3dyn", "", -1)
    ];

    private static readonly string[] IgnoredPrefixes =
        ["_Zn", "_Zd", "_ZNSt", "_ZNKSt", "_ZSt", "__cxa", "_Unwind", "__clang", "__gxx"];

    public static Trace Run(byte[] bin, IBinaryImage img, PltCallResolver calls, IReadOnlyList<MachoSymbols.Symbol> syms,
        ulong start, ulong end, bool stopAtReturn) {
        var list = Arm64DataTableReader.ListRange(bin, start, end, (int)Math.Min((end - start) / 4, 4096));
        if (!list.Ok) return new Trace("", [], list.Diagnostics);

        var template = new StringBuilder();
        var callNames = new List<string>();
        var image = new Arm64Image(bin, img);
        var tracker = new Arm64PageTracker { Image = image };
        bool scaled = false;
        foreach (var i in list.Instructions) {
            if (image.TryWord(i.Va, out uint word)) tracker.Track(i.Va, word);
            var ops = SplitOps(i.Operands);
            switch (i.Mnemonic) {
                case "ret" when stopAtReturn:
                    return new Trace(template.ToString(), callNames, "ok");
                case "add" when ops.Count == 3 && RegIndex(ops[0]) is { } ra && tracker.TryValue(ra, out ulong addr):
                    if (Literal(bin, img, addr) is { } s) template.Append(s);
                    break;
                case "movz" when ops.Count == 3 && TryImm(ops[1], out ulong hi) && (hi << ShiftOf(ops)) == Hundred:
                    scaled = true;
                    break;
                case "bl" when ops.Count == 1 && TryImm(ops[0], out ulong t):
                    Call(calls.Resolve(t), syms, tracker, template, callNames, scaled);
                    tracker.Invalidate(0);
                    tracker.Invalidate(1);
                    tracker.Invalidate(2);
                    scaled = false;
                    break;
            }
        }

        return new Trace(template.ToString(), callNames, "ok");
    }

    private static void Call(ulong target, IReadOnlyList<MachoSymbols.Symbol> syms, Arm64PageTracker tracker,
        StringBuilder template, List<string> callNames, bool scaled) {
        if (!MachoSymbols.TryResolveVa(syms, target, out var fn, out ulong off) || off != 0) return;
        foreach (var (needle, token, argReg) in KnownCalls) {
            if (!fn.Name.Contains(needle, StringComparison.Ordinal)) continue;
            callNames.Add(fn.Name);
            if (token.Length == 0) return;
            template.Append('{').Append(token);
            if (argReg >= 0 && tracker.TryValue(argReg, out ulong dim))
                template.Append(':').Append(dim.ToString(CultureInfo.InvariantCulture));
            template.Append('}');
            return;
        }

        int ts = fn.Name.IndexOf("to_stringE", StringComparison.Ordinal);
        if (ts >= 0) {
            callNames.Add(fn.Name);
            char kind = ts + 10 < fn.Name.Length ? fn.Name[ts + 10] : 'i';
            template.Append(kind is 'd' or 'e' or 'f' ? "{number}" : scaled ? "{int100}" : "{int}");
            return;
        }

        if (IgnoredPrefixes.Any(p => fn.Name.StartsWith(p, StringComparison.Ordinal))) return;
        if (fn.Name.EndsWith("D0Ev", StringComparison.Ordinal) || fn.Name.EndsWith("D1Ev", StringComparison.Ordinal)
                                                                || fn.Name.EndsWith("D2Ev", StringComparison.Ordinal)) return;
        callNames.Add(fn.Name);
        template.Append("{call:").Append(fn.Name).Append('}');
    }

    private static string? Literal(byte[] bin, IBinaryImage img, ulong va) {
        if (!img.TryVaToFileOffset(va, out _, out var owner) || owner.Name is not ("__cstring" or ".rodata")) return null;
        string s = BinaryStrings.ReadCstr(bin, img, va, MaxLiteral);
        if (s.Length == 0) return null;
        foreach (char c in s) {
            if (c is not '\u001b' and (< ' ' or '\u007f')) return null;
        }

        return s;
    }

    private static int? RegIndex(string reg) =>
        reg.Length > 1 && reg[0] is 'x' or 'w' && int.TryParse(reg.AsSpan(1), out int n) ? n : null;

    public readonly record struct Trace(string Template, IReadOnlyList<string> Calls, string Diagnostics);
}
