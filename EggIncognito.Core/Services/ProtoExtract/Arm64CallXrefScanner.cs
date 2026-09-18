namespace EggIncognito.Core.Services.ProtoExtract;

public static class Arm64CallXrefScanner {
    public readonly record struct CallSite(ulong Va, string Via, string Symbol, ulong SymbolOffset, string Section);

    public readonly record struct CallXrefScan(ulong FuncVa, ulong FuncEnd, bool Reachable, int Total, int Returned,
        string Diagnostics, IReadOnlyList<CallSite> Sites);

    private static readonly string[] DataSections = [
        ".data", ".data.rel.ro", ".got", ".got.plt", "__const", "__data", "__got", "__data_const",
    ];

    public static CallXrefScan Scan(byte[] bin, ulong funcVa, int max = 400) {
        if (bin is null || bin.Length < 64) return new CallXrefScan(funcVa, funcVa, false, 0, 0, "binary too short", []);
        var img = BinaryImage.Load(bin);
        if (img is null) return new CallXrefScan(funcVa, funcVa, false, 0, 0, "unrecognized binary format", []);
        if (!img.TryFindText(out int fo, out int size, out ulong vm))
            return new CallXrefScan(funcVa, funcVa, false, 0, 0, "no text section", []);
        if (fo < 0 || size <= 0 || (long)fo + size > bin.Length)
            return new CallXrefScan(funcVa, funcVa, false, 0, 0, "text section out of bounds", []);

        var index = MachoSymbols.Index.Build(img.Symbols);
        ulong funcStart = funcVa;
        ulong funcEnd = funcVa;
        if (index.TryResolve(funcVa, out var fr, out _)) {
            funcStart = fr.Start;
            funcEnd = fr.End;
        }

        var arm = new Arm64Image(bin, img);
        var sites = new List<CallSite>(Math.Min(max, 1024));
        ulong textEnd = vm + (ulong)size;
        int total = 0;

        for (int p = 0; p + 4 <= size; p += 4) {
            ulong va = vm + (ulong)p;
            int b = fo + p;
            uint word = (uint)(bin[b] | (bin[b + 1] << 8) | (bin[b + 2] << 16) | (bin[b + 3] << 24));
            string? via = null;

            if ((word & 0xFC000000) == 0x94000000 && BranchTarget(va, word) == funcVa) {
                via = "bl";
            } else if ((word & 0xFC000000) == 0x14000000 && BranchTarget(va, word) == funcVa
                       && (va < funcStart || va >= funcEnd)) {
                via = "b";
            }

            if (via is null && arm.TryPageRef(va, out ulong target)) {
                if (target == funcVa) {
                    via = "adrp+add";
                } else if ((target < vm || target >= textEnd)
                           && TryReadPtr(img, bin, target, out ulong ptr) && ptr == funcVa) {
                    via = "adrp+ldr->got";
                }
            }

            if (via is null && Arm64Bits.TryAdr(arm, va, out ulong adrTarget, out _) && adrTarget == funcVa)
                via = "adr";

            if (via is null) continue;

            total++;
            if (sites.Count < max) {
                sites.Add(index.TryResolve(va, out var range, out ulong off)
                    ? new CallSite(va, via, range.Name, off, "")
                    : new CallSite(va, via, "", 0, ""));
            }
        }

        foreach (var s in img.Sections) {
            if (s.VmSize == 0 || Array.IndexOf(DataSections, s.Name) < 0) continue;
            int start = s.FileOff;
            long endLong = (long)s.FileOff + (long)s.VmSize;
            int end = (int)Math.Min(endLong, bin.Length);
            if (start < 0 || start >= end) continue;

            for (int off = start; off + 8 <= end; off += 8) {
                if (BitConverter.ToUInt64(bin, off) != funcVa) continue;
                ulong slotVa = s.VmAddr + (ulong)(off - s.FileOff);
                total++;
                if (sites.Count < max) sites.Add(new CallSite(slotVa, "data-ptr", "", 0, s.Name));
            }
        }

        if (img is ElfImage elf) {
            foreach (ulong slotVa in elf.RelocSlotsTargeting(funcVa)) {
                total++;
                if (sites.Count < max) sites.Add(new CallSite(slotVa, "reloc-ptr", "", 0, SectionNameOf(img, slotVa)));
            }
        }

        return new CallXrefScan(funcVa, funcEnd, total > 0, total, sites.Count,
            sites.Count < total ? $"truncated to {max} of {total}" : "ok", sites);
    }

    private static ulong BranchTarget(ulong va, uint word) {
        long imm = word & 0x03FFFFFF;
        if ((imm & 0x02000000) != 0) imm -= 0x04000000;
        return (ulong)((long)va + imm * 4);
    }

    private static string SectionNameOf(IBinaryImage img, ulong va) {
        foreach (var s in img.Sections) {
            if (s.VmSize != 0 && va >= s.VmAddr && va < s.VmAddr + s.VmSize) return s.Name;
        }

        return "";
    }

    private static bool TryReadPtr(IBinaryImage img, byte[] bin, ulong slotVa, out ulong ptr) {
        ptr = 0;
        if (img.TryVaToFileOffset(slotVa, out int fo, out _) && fo >= 0 && fo + 8 <= bin.Length) {
            ptr = BitConverter.ToUInt64(bin, fo);
            if (ptr != 0) return true;
        }

        if (img is ElfImage elf && elf.TryResolveRelative(slotVa, out ulong reloc) && reloc != 0) {
            ptr = reloc;
            return true;
        }

        return false;
    }
}
