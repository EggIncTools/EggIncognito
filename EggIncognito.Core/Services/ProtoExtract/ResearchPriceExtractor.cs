using static EggIncognito.Core.Services.ProtoExtract.Arm64Operands;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class ResearchPriceExtractor {
    public const string InitSymbol = ResearchCatalogExtractor.InitSymbol;
    public const string CommonEnumSymbol = ResearchCatalogExtractor.CommonEnumSymbol;
    public const string EpicEnumSymbol = ResearchCatalogExtractor.EpicEnumSymbol;

    private readonly record struct PriceLayout(ulong MaxLevel, ulong PriceStart, ulong PriceEnd, ulong PricePercent,
        ulong EpicPriceStart, ulong EpicPriceEnd);

    private static readonly PriceLayout AppleLayout = new(0x88, 0x90, 0x98, 0xa0, 0xa8, 0xb0);
    private static readonly PriceLayout NdkLayout = new(0x98, 0xa0, 0xa8, 0xb0, 0xb8, 0xc0);

    public readonly record struct Price(
        bool Epic,
        int MaxLevel,
        double PriceStart,
        double PriceEnd,
        double PricePercent,
        double EpicPriceStart,
        double EpicPriceEnd);

    public readonly record struct Result(bool Ok, IReadOnlyList<Price> Records, string Diagnostics);

    public readonly record struct ByIdResult(bool Ok, IReadOnlyDictionary<string, Price> Prices, string Diagnostics);

    public static ByIdResult ExtractById(byte[] bin, IReadOnlyList<ResearchCatalogExtractor.ResearchEntry> entries) {
        var r = Extract(bin);
        if (!r.Ok) return new ByIdResult(false, new Dictionary<string, Price>(), r.Diagnostics);
        if (entries.Count != r.Records.Count) {
            return new ByIdResult(false, new Dictionary<string, Price>(),
                $"catalog has {entries.Count} rows, price walk has {r.Records.Count}");
        }

        var map = new Dictionary<string, Price>(StringComparer.Ordinal);
        for (int k = 0; k < entries.Count; k++) {
            if (entries[k].Epic != r.Records[k].Epic) {
                return new ByIdResult(false, new Dictionary<string, Price>(),
                    $"epic flag mismatch at record {k} ({entries[k].Id})");
            }

            map[entries[k].Id] = r.Records[k];
        }

        return new ByIdResult(true, map, r.Diagnostics);
    }

    public static Result Extract(byte[] bin) {
        var img = BinaryImage.Load(bin);
        if (img is null) return new Result(false, [], "unrecognized binary");
        bool elf = img is ElfImage;
        var syms = img.Symbols;
        if (!ResearchCatalogExtractor.TryReadEnumArray(bin, syms, CommonEnumSymbol, out ulong commonGlobal,
                out int commonCount, out int stride)) {
            return new Result(false, [], "could not resolve ResearchData::enumForId array global");
        }

        if (!ResearchCatalogExtractor.TryReadEnumArray(bin, syms, EpicEnumSymbol, out ulong epicGlobal,
                out int epicCount, out int epicStride)) {
            return new Result(false, [], "could not resolve ResearchData::enumForEpicId array global");
        }

        if (stride != epicStride)
            return new Result(false, [], $"stride mismatch: common 0x{stride:x}, epic 0x{epicStride:x}");

        Arm64DataTableReader.ListResult lst;
        if (elf) {
            if (!ResearchCatalogExtractor.TryLocateElfInit(bin, out ulong s, out ulong e))
                return new Result(false, [], "researchdata init not located via signature on ELF");
            lst = Arm64DataTableReader.ListRange(bin, s, e, 100_000);
        } else {
            lst = Arm64DataTableReader.ListWith(bin, syms, [InitSymbol], 100_000);
        }

        if (!lst.Ok) return new Result(false, [], lst.Diagnostics);

        var walk = Walk(bin, img, lst.Instructions);
        ulong commonBase;
        ulong epicBase;
        if (elf) {
            if (!ResearchCatalogExtractor.TryResolveElfBases(bin, img, lst.Instructions, stride, commonCount,
                    epicCount, out commonBase, out epicBase)) {
                return new Result(false, [], "could not match research id-runs on ELF");
            }
        } else {
            if (!walk.Addrs.TryGetValue(commonGlobal, out commonBase))
                return new Result(false, [], "init does not store the common research array base");
            if (!walk.Addrs.TryGetValue(epicGlobal, out epicBase))
                return new Result(false, [], "init does not store the epic research array base");
        }

        var lay = elf ? NdkLayout : AppleLayout;
        var records = new List<Price>(commonCount + epicCount);
        var missing = new List<string>();
        for (int k = 0; k < commonCount + epicCount; k++) {
            bool epic = k >= commonCount;
            ulong rec = epic ? epicBase + (ulong)((k - commonCount) * stride) : commonBase + (ulong)(k * stride);
            if (!walk.Ints.TryGetValue(rec + lay.MaxLevel, out long maxLevel)) missing.Add($"{k}:maxLevel");
            ulong[] needed = epic
                ? [lay.EpicPriceStart, lay.EpicPriceEnd]
                : [lay.PriceStart, lay.PriceEnd, lay.PricePercent];
            foreach (ulong off in needed) {
                if (!walk.Bits.ContainsKey(rec + off)) missing.Add($"{k}:0x{off:x}");
            }

            records.Add(new Price(epic, (int)maxLevel, Dbl(walk, rec + lay.PriceStart),
                Dbl(walk, rec + lay.PriceEnd), Dbl(walk, rec + lay.PricePercent),
                Dbl(walk, rec + lay.EpicPriceStart), Dbl(walk, rec + lay.EpicPriceEnd)));
        }

        if (missing.Count > 0)
            return new Result(false, records, $"unresolved price stores: {string.Join(", ", missing)}");
        return new Result(true, records, $"{commonCount}+{epicCount} price records");
    }

    private static double Dbl(WalkResult walk, ulong va) =>
        walk.Bits.TryGetValue(va, out ulong b) ? Arm64Bits.F64(b) : 0;

    private readonly record struct Val(char Kind, ulong Lo, ulong Hi = 0);

    private sealed record WalkResult(Dictionary<ulong, ulong> Addrs, Dictionary<ulong, ulong> Bits,
        Dictionary<ulong, long> Ints);

    private static readonly HashSet<string> NonWriting = [
        "cmp", "cmn", "tst", "fcmp", "ret", "b", "bl", "blr", "br", "cbz", "cbnz", "tbz", "tbnz"
    ];

    private static string Norm(string reg) {
        string r = reg.Trim();
        int dot = r.IndexOf('.');
        if (dot > 0) r = r[..dot];
        if (r.Length < 2 || !char.IsAsciiDigit(r[1])) return r;
        return r[0] switch {
            'w' or 'x' => "x" + r[1..],
            'q' or 'd' or 's' or 'h' or 'b' or 'v' => "v" + r[1..],
            _ => r
        };
    }

    private static WalkResult Walk(byte[] bin, IBinaryImage img, IReadOnlyList<Arm64DataTableReader.Insn> insns) {
        var regs = new Dictionary<string, Val>(StringComparer.Ordinal);
        var frame = new Dictionary<(string, long), Val>();
        var addrs = new Dictionary<ulong, ulong>();
        var bits = new Dictionary<ulong, ulong>();
        var ints = new Dictionary<ulong, long>();

        Val? Get(string reg) {
            if (reg is "xzr" or "wzr") return new Val('i', 0);
            return regs.TryGetValue(Norm(reg), out var v) ? v : null;
        }

        void Set(string reg, Val? v) {
            string n = Norm(reg);
            if (v is { } val) regs[n] = val;
            else regs.Remove(n);
        }

        bool TryRead(ulong va, int size, out ulong lo, out ulong hi) {
            lo = 0;
            hi = 0;
            if (!img.TryVaToFileOffset(va, out int fo, out _) || fo + size > bin.Length) return false;
            if (size == 4) lo = BitConverter.ToUInt32(bin, fo);
            else lo = BitConverter.ToUInt64(bin, fo);
            if (size == 16) hi = BitConverter.ToUInt64(bin, fo + 8);
            return true;
        }

        int SizeOf(string reg, string mnemonic) {
            if (mnemonic.EndsWith('b')) return 1;
            if (mnemonic.EndsWith('h')) return 2;
            return reg.Trim()[0] switch {
                'w' or 's' => 4,
                'x' or 'd' => 8,
                'q' => 16,
                _ => 0
            };
        }

        Val? Load(string baseReg, long disp, int size) {
            if (baseReg is "sp" or "x29") return frame.TryGetValue((baseReg, disp), out var fv) ? fv : null;
            if (Get(baseReg) is not { Kind: 'a' } b || size < 4) return null;
            if (!TryRead(b.Lo + (ulong)disp, size, out ulong lo, out ulong hi)) return null;
            return size == 16 ? new Val('q', lo, hi) : new Val('i', lo);
        }

        void Store(Val? v, ulong target, int size, string reg) {
            if (size == 4) {
                if (v is { Kind: 'i' } iv && reg.Trim()[0] == 'w') ints[target] = (int)(uint)iv.Lo;
                else ints.Remove(target);
                return;
            }

            if (size == 8) {
                if (v is { Kind: 'a' } av) addrs[target] = av.Lo;
                if (v is { Kind: 'i' } iv) bits[target] = iv.Lo;
                else if (v is { Kind: 'q' } qv) bits[target] = qv.Lo;
                else bits.Remove(target);
                return;
            }

            if (size == 16) {
                if (v is { Kind: 'q' } qv) {
                    bits[target] = qv.Lo;
                    bits[target + 8] = qv.Hi;
                } else if (v is { Kind: 'i', Lo: 0 }) {
                    bits[target] = 0;
                    bits[target + 8] = 0;
                } else {
                    bits.Remove(target);
                    bits.Remove(target + 8);
                }
            }
        }

        bool TryAddr(string mem, out string baseReg, out long disp) {
            disp = 0;
            if (!TryMem(mem, out baseReg, out ulong off, out string? idx, out int sh)) return false;
            disp = unchecked((long)off);
            if (idx is null) return true;
            if (Get(idx) is not { Kind: 'i' } iv) return false;
            disp += unchecked((long)iv.Lo) << sh;
            return true;
        }

        void DoStores(IReadOnlyList<string> srcs, string mem, string? postImm, bool writeback, string mnemonic) {
            if (!TryAddr(mem, out string baseReg, out long disp)) return;
            int size = SizeOf(srcs[0], mnemonic);
            bool frameBase = baseReg is "sp" or "x29";
            var b = Get(baseReg);
            for (int s = 0; s < srcs.Count; s++) {
                long at = disp + s * size;
                var v = Get(srcs[s]);
                if (frameBase) {
                    if (v is { } fv) frame[(baseReg, at)] = fv;
                    else frame.Remove((baseReg, at));
                } else if (b is { Kind: 'a' } ab && size >= 4) {
                    Store(v, ab.Lo + (ulong)at, size, srcs[s]);
                }
            }

            if (b is not { Kind: 'a' } wb) return;
            if (writeback) Set(baseReg, wb with { Lo = wb.Lo + (ulong)disp });
            else if (postImm is not null && TryImm(postImm, out long post)) Set(baseReg, wb with { Lo = wb.Lo + (ulong)post });
        }

        foreach (var i in insns) {
            var ops = SplitOps(i.Operands);
            bool writeback = i.Operands.TrimEnd().EndsWith('!');
            switch (i.Mnemonic) {
                case "adrp" when ops.Count == 2 && TryImm(ops[1], out long pg):
                    Set(ops[0], new Val('a', (ulong)pg));
                    break;
                case "add" or "sub" when ops.Count == 3 && TryImm(ops[2], out long k) && Get(ops[1]) is { Kind: 'a' or 'i' } src:
                    Set(ops[0], src with { Lo = i.Mnemonic == "add" ? src.Lo + (ulong)k : src.Lo - (ulong)k });
                    break;
                case "mov" when ops.Count == 2:
                    if (Get(ops[1]) is { } mv) Set(ops[0], mv);
                    else if (TryImm(ops[1], out long mi)) Set(ops[0], new Val('i', (ulong)mi));
                    else Set(ops[0], null);
                    break;
                case "movz" when ops.Count >= 2 && TryImm(ops[1], out long mz):
                    Set(ops[0], new Val('i', (ulong)mz << ShiftOf(ops)));
                    break;
                case "movk" when ops.Count >= 2 && TryImm(ops[1], out long mk) && Get(ops[0]) is { Kind: 'i' } kv: {
                        int shift = ShiftOf(ops);
                        Set(ops[0], kv with { Lo = (kv.Lo & ~(0xFFFFUL << shift)) | ((ulong)mk << shift) });
                        break;
                    }
                case "orr" when ops.Count == 3 && ops[1] is "xzr" or "wzr" && TryImm(ops[2], out long ov):
                    Set(ops[0], new Val('i', (ulong)ov));
                    break;
                case "fmov" when ops.Count == 2:
                    if (ops[0].EndsWith(".2d", StringComparison.Ordinal) && TryFpImm(ops[1], out double vi)
                        && ops[1].TrimStart().StartsWith('#')) {
                        ulong vb = BitConverter.DoubleToUInt64Bits(vi);
                        Set(ops[0], new Val('q', vb, vb));
                    } else if (ops[0].StartsWith('d') && TryFpImm(ops[1], out double fi) && ops[1].TrimStart().StartsWith('#'))
                        Set(ops[0], new Val('i', BitConverter.DoubleToUInt64Bits(fi)));
                    else if (ops[0].StartsWith('d') && Get(ops[1]) is { Kind: 'i' } fx) Set(ops[0], fx);
                    else Set(ops[0], null);
                    break;
                case "movi" when ops.Count == 2:
                    Set(ops[0], ops[1].Trim().TrimStart('#').All(c => c == '0') ? new Val('q', 0, 0) : null);
                    break;
                case "dup" when ops.Count == 2:
                    Set(ops[0], ops[0].EndsWith(".2d", StringComparison.Ordinal) && Get(ops[1]) is { Kind: 'i' } dv
                        ? new Val('q', dv.Lo, dv.Lo)
                        : null);
                    break;
                case "ldr" or "ldur" when ops.Count == 2 && TryAddr(ops[1], out string lb, out long ld):
                    Set(ops[0], Load(lb, ld, SizeOf(ops[0], "ldr")));
                    break;
                case "ldp" when ops.Count >= 3 && TryAddr(ops[2], out string pb, out long pd): {
                        int size = SizeOf(ops[0], "ldp");
                        var first = Load(pb, pd, size);
                        var second = Load(pb, pd + size, size);
                        Set(ops[0], first);
                        Set(ops[1], second);
                        break;
                    }
                case "str" or "stur" or "strb" or "sturb" or "strh" or "sturh" when ops.Count >= 2:
                    DoStores([ops[0]], ops[1], ops.Count >= 3 ? ops[2] : null, writeback, i.Mnemonic);
                    break;
                case "stp" when ops.Count >= 3:
                    DoStores([ops[0], ops[1]], ops[2], ops.Count >= 4 ? ops[3] : null, writeback, i.Mnemonic);
                    break;
                case "bl" or "blr":
                    for (int r = 0; r <= 18; r++) regs.Remove("x" + r);
                    for (int r = 0; r <= 7; r++) regs.Remove("v" + r);
                    for (int r = 16; r <= 31; r++) regs.Remove("v" + r);
                    break;
                default:
                    if (ops.Count >= 1 && !NonWriting.Contains(i.Mnemonic) && !i.Mnemonic.StartsWith("b.", StringComparison.Ordinal))
                        Set(ops[0], null);
                    break;
            }
        }

        return new WalkResult(addrs, bits, ints);
    }
}
