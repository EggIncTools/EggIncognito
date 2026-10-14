using static EggIncognito.Core.Services.ProtoExtract.Arm64Operands;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class ResearchPriceCurveExtractor {
    public const string CtorSymbol = "_ZN10PriceCurveC2ENS_4TypeE";
    private const double StandardSizeScale = 3500;
    private const double VirtueSizeScale = 25000;

    public readonly record struct Result(bool Ok, ResearchPriceCurve.Constants Constants, string Diagnostics);

    private readonly record struct Fp(char Kind, double Value);

    public static Result Extract(byte[] bin) {
        var img = BinaryImage.Load(bin);
        if (img is null) return new Result(false, default, "unrecognized binary");
        var lst = Arm64DataTableReader.ListWith(bin, img.Symbols, [CtorSymbol], 400);
        if (!lst.Ok) return new Result(false, default, lst.Diagnostics);

        var ints = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var page = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var fp = new Dictionary<string, Fp>(StringComparer.Ordinal);
        var frame = new Dictionary<long, Fp>();
        double? standardSize = null, virtueSize = null, tail = null;
        var bases = new List<double>();

        Fp? Get(string reg) => fp.TryGetValue(FpName(reg), out var v) ? v : null;
        void Set(string reg, Fp? v) {
            if (v is { } val) fp[FpName(reg)] = val;
            else fp.Remove(FpName(reg));
        }

        foreach (var i in lst.Instructions) {
            var ops = SplitOps(i.Operands);
            switch (i.Mnemonic) {
                case "adrp" when ops.Count == 2 && TryImm(ops[1], out long pg):
                    page[ops[0]] = (ulong)pg;
                    break;
                case "add" when ops.Count == 3 && page.TryGetValue(ops[1], out ulong ab) && TryImm(ops[2], out long ai):
                    page[ops[0]] = ab + (ulong)ai;
                    break;
                case "movz" when ops.Count >= 2 && TryImm(ops[1], out long mz):
                    ints[ops[0]] = (ulong)mz << ShiftOf(ops);
                    break;
                case "orr" when ops.Count == 3 && ops[1] == "xzr" && TryImm(ops[2], out long ov):
                    ints[ops[0]] = (ulong)ov;
                    break;
                case "movk" when ops.Count >= 2 && TryImm(ops[1], out long mk) && ints.TryGetValue(ops[0], out ulong kv): {
                        int shift = ShiftOf(ops);
                        ints[ops[0]] = (kv & ~(0xFFFFUL << shift)) | ((ulong)mk << shift);
                        break;
                    }
                case "fmov" when ops.Count == 2 && IsFp(ops[0]):
                    if (ints.TryGetValue(ops[1], out ulong fb)) Set(ops[0], new Fp('c', Arm64Bits.F64(fb)));
                    else if (TryFpImm(ops[1], out double fi) && ops[1].TrimStart().StartsWith('#')) Set(ops[0], new Fp('c', fi));
                    else Set(ops[0], Get(ops[1]));
                    break;
                case "mov" when ops.Count == 2 && ops[0].StartsWith('v'):
                    Set(ops[0], Get(ops[1]));
                    break;
                case "ldr" when ops.Count == 2 && IsFp(ops[0]) && TryMem(ops[1], out string lb, out ulong ld):
                    if (lb == "sp") Set(ops[0], frame.TryGetValue((long)ld, out var fv) ? fv : null);
                    else if (page.TryGetValue(lb, out ulong pv) && new Arm64Image(bin, img).TryF64(pv + ld, out double lv))
                        Set(ops[0], new Fp('c', lv));
                    else Set(ops[0], null);
                    break;
                case "str" when ops.Count == 2 && TryMem(ops[1], out string sb, out ulong sd) && sb == "sp":
                    if (IsFp(ops[0]) && Get(ops[0]) is { } sv) frame[(long)sd] = sv;
                    else if (ints.TryGetValue(ops[0], out ulong si)) frame[(long)sd] = new Fp('c', Arm64Bits.F64(si));
                    break;
                case "bl":
                    if (Get("d0") is { Kind: 'c' } pb && pb.Value != 10 && !bases.Contains(pb.Value)) bases.Add(pb.Value);
                    Set("d0", Get("d0") is { Kind: 'c' } pc ? new Fp('p', pc.Value) : new Fp('p', double.NaN));
                    for (int r = 1; r <= 7; r++) fp.Remove("v" + r);
                    for (int r = 16; r <= 31; r++) fp.Remove("v" + r);
                    break;
                case "fmadd" when ops.Count == 4:
                    if (Get(ops[1]) is { Kind: 'p' } sp && Get(ops[2]) is { Kind: 'c' } scale) {
                        if (scale.Value == StandardSizeScale) standardSize ??= sp.Value;
                        else if (scale.Value == VirtueSizeScale) virtueSize ??= sp.Value;
                    }

                    Set(ops[0], null);
                    break;
                case "fmul" when ops.Count == 3: {
                        var a = Get(ops[1]);
                        var b = Get(ops[2]);
                        bool aPow = a is { Kind: 'p' };
                        Fp? pow = aPow ? a : b is { Kind: 'p' } ? b : null;
                        Fp? other = aPow ? b : a;
                        if (pow is { } p && other is { Kind: 'c' } c && double.IsNaN(p.Value) && c.Value > 1) tail ??= c.Value;
                        Set(ops[0], null);
                        break;
                    }
                default:
                    if (ops.Count >= 1 && IsFp(ops[0]) && i.Mnemonic is not ("fcmp" or "str" or "stp" or "stur")) Set(ops[0], null);
                    break;
            }
        }

        var valueBases = bases.Where(b => b != standardSize).ToList();
        double? standardValue = valueBases.Count == 2 ? valueBases[0] : null;
        double? virtueValue = valueBases.Count == 2 ? valueBases[1] : null;
        if (standardSize is null || virtueSize is null || standardValue is null || virtueValue is null || tail is null) {
            return new Result(false, default,
                $"PriceCurve constants unresolved: size {standardSize}/{virtueSize}, value {standardValue}/{virtueValue}, tail {tail}");
        }

        if (standardSize != virtueSize)
            return new Result(false, default, $"size base differs by type: {standardSize} vs {virtueSize}");

        return new Result(true, new ResearchPriceCurve.Constants(standardSize.Value, standardValue.Value, virtueValue.Value, tail.Value),
            $"size {standardSize}, standard {standardValue}, virtue {virtueValue}, tail {tail}");
    }

    private static bool IsFp(string reg) => reg.Trim() is { Length: > 1 } r && r[0] is 'd' or 'v' or 's' && char.IsAsciiDigit(r[1]);

    private static string FpName(string reg) {
        string r = reg.Trim();
        int dot = r.IndexOf('.');
        if (dot > 0) r = r[..dot];
        return "v" + r[1..];
    }
}
