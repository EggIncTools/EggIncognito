using System.Text;
using static EggIncognito.Core.Services.ProtoExtract.Arm64Operands;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class DimensionLabelExtractor {
    public const string Symbol = "_ZN14GameDimensions8name_strEP14GameControllerNS_4NameE";
    private const int CaseBudget = 24;
    private const int SsoCapacity = 23;
    private const ulong TraceSpan = 0x140;

    private static readonly (int Index, string Label)[] Anchors =
        [(3, "away earnings"), (12, "bonus per Soul Egg"), (14, "drone frequency")];

    public static Result Extract(byte[] bin) => ExtractWith(bin, BinaryImage.Load(bin)?.Symbols ?? []);

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms) {
        var img = BinaryImage.Load(bin);
        if (img is null) return Fail("no binary image");
        syms = syms.Count > 0 ? syms : img.Symbols;
        var head = Arm64DataTableReader.ListWith(bin, syms, [Symbol], 32);
        if (!head.Ok) return Fail(head.Diagnostics);

        if (!TryReadSwitch(head.Instructions, out int count, out ulong tableVa, out ulong baseVa))
            return Fail("name_str jump table not recognised");

        bool shortFirst = img is ElfImage;
        var calls = new PltCallResolver(bin, img);
        var labels = new Dictionary<int, string>();
        for (int i = 0; i < count; i++) {
            if (!img.TryVaToFileOffset(tableVa + (ulong)(2 * i), out int fo, out _) || fo + 2 > bin.Length)
                return Fail($"jump table entry {i} unreadable");
            ulong target = baseVa + ((ulong)BitConverter.ToUInt16(bin, fo) << 2);
            string? label = ReadCase(bin, img, target, shortFirst);
            if (label is null || label.EndsWith(' ')) label = TraceCase(bin, img, calls, syms, target) ?? label;
            if (label is null || label.Length == 0 || label.EndsWith(' ') || label == "unknown") continue;
            labels[i] = label;
        }

        var misses = Anchors.Where(a => !labels.TryGetValue(a.Index, out string? l) || l != a.Label)
            .Select(a => $"anchor [{a.Index}] expected '{a.Label}'")
            .ToList();
        return misses.Count == 0
            ? new Result(true, labels, $"{labels.Count} of {count} dimensions labelled")
            : new Result(false, labels, string.Join("; ", misses));
    }

    private static bool TryReadSwitch(IReadOnlyList<Arm64DataTableReader.Insn> insns, out int count,
        out ulong tableVa, out ulong baseVa) {
        count = 0;
        tableVa = 0;
        baseVa = 0;
        var page = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var i in insns) {
            var ops = SplitOps(i.Operands);
            switch (i.Mnemonic) {
                case "cmp" when count == 0 && ops.Count == 2 && ops[0].StartsWith('w') && TryImm(ops[1], out ulong max):
                    count = (int)max + 1;
                    break;
                case "adrp" when ops.Count == 2 && TryImm(ops[1], out ulong pg):
                    page[ops[0]] = pg;
                    break;
                case "add" when tableVa == 0 && ops.Count == 3 && page.TryGetValue(ops[1], out ulong pb)
                                && TryImm(ops[2], out ulong off):
                    tableVa = pb + off;
                    break;
                case "adr" when ops.Count == 2 && TryImm(ops[1], out ulong a):
                    baseVa = a;
                    break;
            }

            if (count > 0 && tableVa != 0 && baseVa != 0) return true;
        }

        return false;
    }

    private static string? TraceCase(byte[] bin, IBinaryImage img, PltCallResolver calls,
        IReadOnlyList<MachoSymbols.Symbol> syms, ulong start) {
        var trace = Arm64StringTracer.Run(bin, img, calls, syms, start, start + TraceSpan, true);
        return trace.Template.Contains("{currency}", StringComparison.Ordinal) ? trace.Template : null;
    }

    private static string? ReadCase(byte[] bin, IBinaryImage img, ulong start, bool shortFirst) {
        var page = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var imm = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var inline = new byte?[SsoCapacity + 1];
        ulong pc = start;
        int budget = CaseBudget;
        while (budget > 0) {
            var chunk = Arm64DataTableReader.ListRange(bin, pc, pc + (ulong)(4 * budget), budget);
            if (!chunk.Ok || chunk.Instructions.Count == 0) return null;
            ulong? jump = null;
            foreach (var i in chunk.Instructions) {
                budget--;
                var ops = SplitOps(i.Operands);
                switch (i.Mnemonic) {
                    case "ret":
                    case "b.eq" or "b.ne":
                        return Compose(inline, shortFirst);
                    case "b" when ops.Count == 1 && TryImm(ops[0], out ulong t):
                        jump = t;
                        break;
                    case "adrp" when ops.Count == 2 && TryImm(ops[1], out ulong pg):
                        page[ops[0]] = pg;
                        break;
                    case "add" when ops.Count == 3 && page.TryGetValue(ops[1], out ulong pb)
                                    && TryImm(ops[2], out ulong off):
                        ulong va = pb + off;
                        if (img.TryVaToFileOffset(va, out _, out var owner) && owner.Name is "__cstring" or ".rodata")
                            return BinaryStrings.ReadCstr(bin, img, va, 96);
                        page[ops[0]] = va;
                        break;
                    case "movz" when ops.Count >= 2 && TryImm(ops[1], out ulong z):
                        imm[RegNum(ops[0])] = z << ShiftOf(ops);
                        break;
                    case "movk" when ops.Count >= 2 && TryImm(ops[1], out ulong k):
                        int sh = ShiftOf(ops);
                        ulong prev = imm.GetValueOrDefault(RegNum(ops[0]));
                        imm[RegNum(ops[0])] = (prev & ~(0xFFFFUL << sh)) | (k << sh);
                        break;
                    case "str" or "stur" or "strh" or "sturh" or "strb" or "sturb":
                        StoreInline(i.Mnemonic, ops, imm, inline);
                        break;
                }

                if (jump is not null) break;
            }

            if (jump is not { } next) return null;
            pc = next;
        }

        return null;
    }

    private static void StoreInline(string mnemonic, List<string> ops, Dictionary<string, ulong> imm,
        byte?[] inline) {
        if (ops.Count < 2 || !TryMem(ops[^1], out string reg, out ulong off) || reg != "x8") return;
        int width = mnemonic switch {
            "strb" or "sturb" => 1,
            "strh" or "sturh" => 2,
            _ => ops[0].StartsWith('x') ? 8 : 4
        };
        ulong v = 0;
        if (!IsZeroReg(ops[0]) && !imm.TryGetValue(RegNum(ops[0]), out v)) return;
        for (int k = 0; k < width; k++) {
            long at = (long)off + k;
            if (at >= 0 && at < inline.Length) inline[at] = (byte)(v >> (8 * k));
        }
    }

    private static string? Compose(byte?[] inline, bool shortFirst) {
        int from = 0;
        int length = SsoCapacity;
        if (shortFirst) {
            if (inline[0] is not { } head || (head & 1) != 0) return null;
            from = 1;
            length = head >> 1;
        }

        var sb = new StringBuilder();
        for (int k = from; k < from + length && k < inline.Length; k++) {
            if (inline[k] is not { } c) return null;
            if (c == 0) break;
            sb.Append((char)c);
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    private static Result Fail(string diagnostics) => new(false, new Dictionary<int, string>(), diagnostics);

    public readonly record struct Result(bool Ok, IReadOnlyDictionary<int, string> Labels, string Diagnostics);
}
