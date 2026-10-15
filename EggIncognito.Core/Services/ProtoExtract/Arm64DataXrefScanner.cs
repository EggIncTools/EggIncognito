namespace EggIncognito.Core.Services.ProtoExtract;

public static class Arm64DataXrefScanner {
    public readonly record struct DataSite(ulong Va, string Via, string Access, string Symbol, ulong SymbolOffset, ulong SlotVa);

    public readonly record struct DataXrefScan(ulong TargetVa, int Total, int Returned, string Diagnostics, IReadOnlyList<DataSite> Sites);

    private const int InstructionWindow = 8;
    private const int GotFollowWindow = 6;

    public static DataXrefScan Scan(byte[] bin, ulong targetVa, int max = 400) {
        if (bin is null || bin.Length < 64) return new DataXrefScan(targetVa, 0, 0, "binary too short", []);
        var img = BinaryImage.Load(bin);
        if (img is null) return new DataXrefScan(targetVa, 0, 0, "unrecognized binary format", []);
        if (!img.TryFindText(out int fo, out int size, out ulong vm))
            return new DataXrefScan(targetVa, 0, 0, "no text section", []);
        if (fo < 0 || size <= 0 || (long)fo + size > bin.Length)
            return new DataXrefScan(targetVa, 0, 0, "text section out of bounds", []);

        var index = CodeIndex.Build(img);
        var arm = new Arm64Image(bin, img);
        var sites = new List<DataSite>(Math.Min(max, 1024));
        int total = 0;

        for (int p = 0; p + 4 <= size; p += 4) {
            ulong adrpVa = vm + (ulong)p;
            if (!arm.TryWord(adrpVa, out uint adrpWord) || !Arm64Bits.TryAdrp(adrpVa, adrpWord, out ulong page, out int rd))
                continue;

            for (int k = 1; k <= InstructionWindow; k++) {
                ulong iva = adrpVa + (ulong)(4 * k);
                if (!arm.TryWord(iva, out uint w)) break;

                if (Arm64Bits.TryAdrp(iva, w, out _, out int clobberRd) && clobberRd == rd) break;

                if (Arm64Bits.TryAddImm(w, out _, out int addRn, out ulong addImm) && addRn == rd) {
                    if (page + addImm == targetVa)
                        AddSite(sites, ref total, max, adrpVa, "adrp+add", "addr", index, 0);
                    break;
                }

                if (Arm64Bits.TryStore(w, out var store) && store.Rn == rd) {
                    ulong ea = page + (ulong)store.Offset;
                    if (ea == targetVa)
                        AddSite(sites, ref total, max, adrpVa, "adrp+str", "write", index, 0);
                    break;
                }

                if (Arm64Bits.TryLoadFp(w, out var fload) && fload.Rn == rd) {
                    ulong ea = page + (ulong)fload.Offset;
                    if (ea == targetVa)
                        AddSite(sites, ref total, max, adrpVa, "adrp+ldr", "read", index, 0);
                    break;
                }

                if (Arm64Bits.TryLoadGpr(w, out var gload) && gload.Rn == rd) {
                    ulong ea = page + (ulong)gload.Offset;
                    if (gload.Rt == rd) {
                        if (TryGotTarget(img, bin, ea, out ulong gotTarget) && gotTarget == targetVa) {
                            string access = FollowGotAccess(arm, iva, rd);
                            AddSite(sites, ref total, max, adrpVa, "got", access, index, ea);
                        }
                    } else if (ea == targetVa) {
                        AddSite(sites, ref total, max, adrpVa, "adrp+ldr", "read", index, 0);
                    }

                    break;
                }
            }
        }

        return new DataXrefScan(targetVa, total, sites.Count,
            sites.Count < total ? $"truncated to {max} of {total}" : "ok", sites);
    }

    private static void AddSite(List<DataSite> sites, ref int total, int max, ulong va, string via, string access,
        CodeIndex index, ulong slotVa) {
        total++;
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
