namespace EggIncognito.Core.Services.ProtoExtract;

public static class Arm64DataXrefScanner {
    public readonly record struct DataSite(ulong Va, string Via, string Access, string Symbol, ulong SymbolOffset, ulong SlotVa);

    public readonly record struct DataXrefScan(ulong TargetVa, int Total, int Returned, string Diagnostics, IReadOnlyList<DataSite> Sites);

    private const int InstructionWindow = 8;
    private const int GotFollowWindow = 6;

    public static DataXrefScan Scan(byte[] bin, ulong targetVa, int max = 400) =>
        ScanMany(bin, [targetVa], max).TryGetValue(targetVa, out var scan)
            ? scan
            : new DataXrefScan(targetVa, 0, 0, "scan failed", []);

    public static IReadOnlyDictionary<ulong, DataXrefScan> ScanMany(byte[] bin, IReadOnlyCollection<ulong> targets,
        int max = 400) {
        var outp = new Dictionary<ulong, DataXrefScan>();
        string? fail = bin is null || bin.Length < 64 ? "binary too short" : null;
        var img = fail is null ? BinaryImage.Load(bin!) : null;
        if (fail is null && img is null) fail = "unrecognized binary format";
        int fo = 0, size = 0;
        ulong vm = 0;
        if (fail is null && !img!.TryFindText(out fo, out size, out vm)) fail = "no text section";
        if (fail is null && (fo < 0 || size <= 0 || (long)fo + size > bin!.Length)) fail = "text section out of bounds";
        if (fail is not null) {
            foreach (ulong t in targets) outp[t] = new DataXrefScan(t, 0, 0, fail, []);
            return outp;
        }

        var wanted = new HashSet<ulong>(targets);
        var index = CodeIndex.Build(img!);
        var arm = new Arm64Image(bin!, img!);
        var sites = new Dictionary<ulong, List<DataSite>>();
        var totals = new Dictionary<ulong, int>();
        foreach (ulong t in wanted) {
            sites[t] = [];
            totals[t] = 0;
        }

        for (int p = 0; p + 4 <= size; p += 4) {
            ulong adrpVa = vm + (ulong)p;
            if (!arm.TryWord(adrpVa, out uint adrpWord) || !Arm64Bits.TryAdrp(adrpVa, adrpWord, out ulong page, out int rd))
                continue;

            for (int k = 1; k <= InstructionWindow; k++) {
                ulong iva = adrpVa + (ulong)(4 * k);
                if (!arm.TryWord(iva, out uint w)) break;

                if (Arm64Bits.TryAdrp(iva, w, out _, out int clobberRd) && clobberRd == rd) break;

                if (Arm64Bits.TryAddImm(w, out _, out int addRn, out ulong addImm) && addRn == rd) {
                    ulong ea = page + addImm;
                    if (wanted.Contains(ea))
                        AddSite(sites[ea], totals, ea, max, adrpVa, "adrp+add", "addr", index, 0);
                    break;
                }

                if (Arm64Bits.TryStore(w, out var store) && store.Rn == rd) {
                    ulong ea = page + (ulong)store.Offset;
                    if (wanted.Contains(ea))
                        AddSite(sites[ea], totals, ea, max, adrpVa, "adrp+str", "write", index, 0);
                    break;
                }

                if (Arm64Bits.TryLoadFp(w, out var fload) && fload.Rn == rd) {
                    ulong ea = page + (ulong)fload.Offset;
                    if (wanted.Contains(ea))
                        AddSite(sites[ea], totals, ea, max, adrpVa, "adrp+ldr", "read", index, 0);
                    break;
                }

                if (Arm64Bits.TryLoadGpr(w, out var gload) && gload.Rn == rd) {
                    ulong ea = page + (ulong)gload.Offset;
                    if (gload.Rt == rd) {
                        if (TryGotTarget(img!, bin!, ea, out ulong gotTarget) && wanted.Contains(gotTarget)) {
                            string access = FollowGotAccess(arm, iva, rd);
                            AddSite(sites[gotTarget], totals, gotTarget, max, adrpVa, "got", access, index, ea);
                        }
                    } else if (wanted.Contains(ea)) {
                        AddSite(sites[ea], totals, ea, max, adrpVa, "adrp+ldr", "read", index, 0);
                    }

                    break;
                }
            }
        }

        foreach (ulong t in wanted) {
            var list = sites[t];
            int total = totals[t];
            outp[t] = new DataXrefScan(t, total, list.Count, list.Count < total ? $"truncated to {max} of {total}" : "ok", list);
        }

        return outp;
    }

    private static void AddSite(List<DataSite> sites, Dictionary<ulong, int> totals, ulong target, int max, ulong va,
        string via, string access, CodeIndex index, ulong slotVa) {
        totals[target]++;
        if (sites.Count >= max) return;
        string symbol = "";
        ulong off = 0;
        if (index.TryResolve(va, out var range, out ulong o)) {
            symbol = range.Name;
            off = o;
        }

        sites.Add(new DataSite(va, via, access, symbol, off, slotVa));
    }

    private static bool TryGotTarget(IBinaryImage img, byte[] bin, ulong slotVa, out ulong target) {
        if (img is ElfImage elf && elf.TryResolveSlot(slotVa, out ulong t, out _)) {
            target = t;
            return true;
        }

        if (img.TryVaToFileOffset(slotVa, out int fo, out _) && fo >= 0 && fo + 8 <= bin.Length) {
            target = BitConverter.ToUInt64(bin.AsSpan(fo));
            return target != 0;
        }

        target = 0;
        return false;
    }

    private static string FollowGotAccess(Arm64Image arm, ulong ldrVa, int reg) {
        for (int k = 1; k <= GotFollowWindow; k++) {
            ulong iva = ldrVa + (ulong)(4 * k);
            if (!arm.TryWord(iva, out uint w)) break;
            if (Arm64Bits.TryLoadGpr(w, out var l) && l.Rn == reg) return "read";
            if (Arm64Bits.TryLoadFp(w, out var lf) && lf.Rn == reg) return "read";
            if (Arm64Bits.TryStore(w, out var s) && s.Rn == reg) return "write";
        }

        return "addr";
    }
}
