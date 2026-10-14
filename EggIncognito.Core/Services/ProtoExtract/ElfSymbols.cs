using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class ElfSymbols {
    private const uint ShtSymtab = 2;
    private const uint ShtDynsym = 11;
    private const int Elf64SymSize = 24;

    public static IReadOnlyList<MachoSymbols.Symbol> Read(byte[] bin) {
        var outp = new List<MachoSymbols.Symbol>();
        if (ElfSections.IsElf32Le(bin)) return Read32(bin);
        if (!ElfSections.IsElf64Le(bin)) return outp;
        try {
            ulong shoff = ElfSections.U64(bin, 0x28);
            int shentsize = ElfSections.U16(bin, 0x3A);
            int shnum = ElfSections.U16(bin, 0x3C);
            if (shentsize < 64 || shnum <= 0) return outp;

            var seen = new HashSet<(string, ulong)>();
            for (int i = 0; i < shnum; i++) {
                long h = (long)shoff + (long)i * shentsize;
                if (h < 0 || h + 64 > bin.Length) break;
                uint type = ElfSections.U32(bin, (int)h + 0x04);
                if (type is not (ShtSymtab or ShtDynsym)) continue;

                ulong tabOff = ElfSections.U64(bin, (int)h + 0x18);
                ulong tabSize = ElfSections.U64(bin, (int)h + 0x20);
                uint link = ElfSections.U32(bin, (int)h + 0x28);
                ulong entsize = ElfSections.U64(bin, (int)h + 0x38);
                if (entsize < Elf64SymSize) entsize = Elf64SymSize;
                if (link >= (uint)shnum) continue;

                long lh = (long)shoff + (long)link * shentsize;
                if (lh < 0 || lh + 64 > bin.Length) continue;
                ulong strOff = ElfSections.U64(bin, (int)lh + 0x18);
                ulong strSize = ElfSections.U64(bin, (int)lh + 0x20);

                ReadSymTab(bin, tabOff, tabSize, entsize, strOff, strSize, seen, outp);
            }
        } catch {
            return outp;
        }

        return outp;
    }

    private const int Elf32SymSize = 16;

    private static List<MachoSymbols.Symbol> Read32(byte[] bin) {
        var outp = new List<MachoSymbols.Symbol>();
        try {
            uint shoff = ElfSections.U32(bin, 0x20);
            int shentsize = ElfSections.U16(bin, 0x2E);
            int shnum = ElfSections.U16(bin, 0x30);
            if (shentsize < 40 || shnum <= 0) return outp;

            var seen = new HashSet<(string, ulong)>();
            for (int i = 0; i < shnum; i++) {
                long h = (long)shoff + (long)i * shentsize;
                if (h < 0 || h + 40 > bin.Length) break;
                uint type = ElfSections.U32(bin, (int)h + 0x04);
                if (type is not (ShtSymtab or ShtDynsym)) continue;

                uint tabOff = ElfSections.U32(bin, (int)h + 0x10);
                uint tabSize = ElfSections.U32(bin, (int)h + 0x14);
                uint link = ElfSections.U32(bin, (int)h + 0x18);
                uint entsize = ElfSections.U32(bin, (int)h + 0x24);
                if (entsize < Elf32SymSize) entsize = Elf32SymSize;
                if (link >= (uint)shnum) continue;

                long lh = (long)shoff + (long)link * shentsize;
                if (lh < 0 || lh + 40 > bin.Length) continue;
                uint strOff = ElfSections.U32(bin, (int)lh + 0x10);
                uint strSize = ElfSections.U32(bin, (int)lh + 0x14);

                uint count = entsize == 0 ? 0 : tabSize / entsize;
                for (uint s = 0; s < count; s++) {
                    long e = (long)tabOff + (long)(s * entsize);
                    if (e < 0 || e + Elf32SymSize > bin.Length) break;
                    uint nameOff = ElfSections.U32(bin, (int)e + 0x00);
                    uint value = ElfSections.U32(bin, (int)e + 0x04);
                    byte info = bin[e + 0x0C];
                    ushort shndx = ElfSections.U16(bin, (int)e + 0x0E);
                    if (nameOff == 0 || nameOff >= strSize) continue;
                    string name = Cstr(bin, (long)strOff + nameOff);
                    if (name.Length == 0) continue;
                    if (!seen.Add((name, value))) continue;
                    outp.Add(new MachoSymbols.Symbol(name, value, info, (byte)shndx));
                }
            }
        } catch {
            return outp;
        }

        return outp;
    }

    private static void ReadSymTab(byte[] bin, ulong tabOff, ulong tabSize, ulong entsize, ulong strOff,
        ulong strSize, HashSet<(string, ulong)> seen, List<MachoSymbols.Symbol> outp) {
        if (entsize == 0) return;
        ulong count = tabSize / entsize;
        for (ulong i = 0; i < count; i++) {
            long e = (long)tabOff + (long)(i * entsize);
            if (e < 0 || e + Elf64SymSize > bin.Length) return;
            uint nameOff = ElfSections.U32(bin, (int)e + 0x00);
            byte info = bin[e + 4];
            ushort shndx = ElfSections.U16(bin, (int)e + 6);
            ulong value = ElfSections.U64(bin, (int)e + 8);
            if (nameOff == 0 || nameOff >= strSize) continue;
            string name = Cstr(bin, (long)strOff + nameOff);
            if (name.Length == 0) continue;
            if (!seen.Add((name, value))) continue;
            outp.Add(new MachoSymbols.Symbol(name, value, info, (byte)shndx));
        }
    }

    private static string Cstr(byte[] b, long o) {
        if (o < 0 || o >= b.Length) return "";
        long end = o;
        while (end < b.Length && b[end] != 0) end++;
        return Encoding.UTF8.GetString(b, (int)o, (int)(end - o));
    }
}
