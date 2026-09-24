namespace EggIncognito.Core.Services.ProtoExtract;

public static class Arm64StringXrefScanner {
    public readonly record struct XrefSite(ulong Va, string Via, string Symbol, ulong SymbolOffset);

    public readonly record struct XrefScan(int Total, int Returned, string Diagnostics, IReadOnlyList<XrefSite> Sites);

    private const ulong PtrMask = 0x0000_FFFF_FFFF_FFFFUL;

    public static XrefScan Scan(byte[] bin, ulong targetVa, int max = 200) {
        if (bin is null || bin.Length < 64) return new XrefScan(0, 0, "binary too short", []);
        var img = BinaryImage.Load(bin);
        if (img is null) return new XrefScan(0, 0, "unrecognized binary format", []);
        if (!img.TryFindText(out int fo, out int size, out ulong vm))
            return new XrefScan(0, 0, "no text section", []);
        if (fo < 0 || size <= 0 || (long)fo + size > bin.Length)
            return new XrefScan(0, 0, "text section out of bounds", []);

        var index = MachoSymbols.Index.Build(img.Symbols);
        var arm = new Arm64Image(bin, img);
        var sites = new List<XrefSite>(Math.Min(max, 1024));
        int total = 0;

        for (int p = 0; p + 4 <= size; p += 4) {
            ulong va = vm + (ulong)p;
            string? via = null;

            if (arm.TryPageRef(va, out ulong target)) {
                if (target == targetVa) {
                    via = "adrp+add/ldr";
                } else if ((target < vm || target >= vm + (ulong)size)
                           && TryReadPtr(img, bin, target, out ulong ptr)
                           && (ptr & PtrMask) == (targetVa & PtrMask)) {
                    via = "adrp+ldr->ptr";
                }
            }

            if (via is null && Arm64Bits.TryAdr(arm, va, out ulong adrTarget, out _) && adrTarget == targetVa)
                via = "adr";

            if (via is null) continue;

            total++;
            if (sites.Count < max) {
                sites.Add(index.TryResolve(va, out var range, out ulong off)
                    ? new XrefSite(va, via, range.Name, off)
                    : new XrefSite(va, via, "", 0));
            }
        }

        return new XrefScan(total, sites.Count, sites.Count < total ? $"truncated to {max} of {total}" : "ok", sites);
    }

    private static bool TryReadPtr(IBinaryImage img, byte[] bin, ulong slotVa, out ulong ptr) {
        ptr = 0;
        if (img.TryVaToFileOffset(slotVa, out int fo, out _) && fo >= 0 && fo + 8 <= bin.Length) {
            ptr = BitConverter.ToUInt64(bin.AsSpan(fo));
            if (ptr != 0) return true;
        }

        if (img is ElfImage elf && elf.TryResolveRelative(slotVa, out ulong reloc) && reloc != 0) {
            ptr = reloc;
            return true;
        }

        return false;
    }
}
