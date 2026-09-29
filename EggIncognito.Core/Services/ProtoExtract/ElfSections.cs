using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class ElfSections {
    private const uint PtLoad = 1;
    private const uint ShtInitArray = 14;
    private const uint ShtRela = 4;
    private const uint RAarch64Relative = 1027;
    private const uint RAarch64JumpSlot = 1026;
    private const int Elf64SymSize = 24;
    private const ulong ShfAlloc = 0x2;

    public static IReadOnlyList<MachoSections.Section> Read(byte[] bin) {
        var result = new List<MachoSections.Section>();
        if (!IsElf64Le(bin)) return result;
        try {
            ulong shoff = U64(bin, 0x28);
            int shentsize = U16(bin, 0x3A);
            int shnum = U16(bin, 0x3C);
            int shstrndx = U16(bin, 0x3E);
            if (shentsize < 64 || shnum <= 0 || shstrndx >= shnum) return result;

            long strHdr = (long)shoff + (long)shstrndx * shentsize;
            if (strHdr < 0 || strHdr + 64 > bin.Length) return result;
            long strTab = (long)U64(bin, (int)strHdr + 0x18);

            for (int i = 0; i < shnum; i++) {
                long h = (long)shoff + (long)i * shentsize;
                if (h < 0 || h + 64 > bin.Length) break;
                ulong flags = U64(bin, (int)h + 0x08);
                if ((flags & ShfAlloc) == 0) continue;
                uint nameOff = U32(bin, (int)h + 0x00);
                ulong addr = U64(bin, (int)h + 0x10);
                ulong off = U64(bin, (int)h + 0x18);
                ulong sz = U64(bin, (int)h + 0x20);
                string name = Cstr(bin, strTab + nameOff);
                result.Add(new MachoSections.Section("", name, addr, sz, (int)off, 0));
            }
        } catch {
            return result;
        }

        return result;
    }

    public static IReadOnlyList<LoadSegment> ReadSegments(byte[] bin) {
        var result = new List<LoadSegment>();
        if (IsElf32Le(bin)) return ReadSegments32(bin);
        if (!IsElf64Le(bin)) return result;
        try {
            ulong phoff = U64(bin, 0x20);
            int phentsize = U16(bin, 0x36);
            int phnum = U16(bin, 0x38);
            if (phentsize < 56 || phnum <= 0) return result;

            for (int i = 0; i < phnum; i++) {
                long p = (long)phoff + (long)i * phentsize;
                if (p < 0 || p + 56 > bin.Length) break;
                if (U32(bin, (int)p + 0x00) != PtLoad) continue;
                ulong off = U64(bin, (int)p + 0x08);
                ulong vaddr = U64(bin, (int)p + 0x10);
                ulong filesz = U64(bin, (int)p + 0x20);
                ulong memsz = U64(bin, (int)p + 0x28);
                result.Add(new LoadSegment(vaddr, memsz, (long)off, (long)filesz));
            }
        } catch {
            return result;
        }

        return result;
    }

    private static List<LoadSegment> ReadSegments32(byte[] bin) {
        var result = new List<LoadSegment>();
        try {
            uint phoff = U32(bin, 0x1C);
            int phentsize = U16(bin, 0x2A);
            int phnum = U16(bin, 0x2C);
            if (phentsize < 32 || phnum <= 0) return result;

            for (int i = 0; i < phnum; i++) {
                long p = (long)phoff + (long)i * phentsize;
                if (p < 0 || p + 32 > bin.Length) break;
                if (U32(bin, (int)p + 0x00) != PtLoad) continue;
                uint off = U32(bin, (int)p + 0x04);
                uint vaddr = U32(bin, (int)p + 0x08);
                uint filesz = U32(bin, (int)p + 0x10);
                uint memsz = U32(bin, (int)p + 0x14);
                result.Add(new LoadSegment(vaddr, memsz, off, filesz));
            }
        } catch {
            return result;
        }

        return result;
    }

    public static bool TryVaToFileOffset(IReadOnlyList<LoadSegment> segments, ulong va, out int fileOff) {
        foreach (var s in segments) {
            if (s.FileSize == 0) continue;
            if (va >= s.VAddr && va < s.VAddr + (ulong)s.FileSize) {
                long off = s.FileOff + (long)(va - s.VAddr);
                if (off is >= 0 and <= int.MaxValue) {
                    fileOff = (int)off;
                    return true;
                }
            }
        }

        fileOff = 0;
        return false;
    }

    public static bool TryFindInitArray(byte[] bin, out ulong va, out ulong size) {
        va = 0;
        size = 0;
        if (!IsElf64Le(bin)) return false;
        try {
            ulong shoff = U64(bin, 0x28);
            int shentsize = U16(bin, 0x3A);
            int shnum = U16(bin, 0x3C);
            if (shentsize < 64 || shnum <= 0) return false;

            for (int i = 0; i < shnum; i++) {
                long h = (long)shoff + (long)i * shentsize;
                if (h < 0 || h + 64 > bin.Length) break;
                if (U32(bin, (int)h + 0x04) != ShtInitArray) continue;
                va = U64(bin, (int)h + 0x10);
                size = U64(bin, (int)h + 0x20);
                return size != 0;
            }
        } catch {
            return false;
        }

        return false;
    }

    public static IReadOnlyList<ulong> ReadInitArrayTargets(byte[] bin) {
        if (!TryFindInitArray(bin, out var va, out var size)) return [];
        var segments = ReadSegments(bin);
        if (!TryVaToFileOffset(segments, va, out var baseFo)) return [];

        var relocs = ReadRelativeRelocs(bin, va, va + size);
        var n = (int)(size / 8);
        var outp = new List<ulong>(n);
        for (int i = 0; i < n; i++) {
            long p = (long)baseFo + i * 8;
            if (p < 0 || p + 8 > bin.Length) break;
            ulong slotVa = va + (ulong)(i * 8);
            ulong val = U64(bin, (int)p);
            if (val == 0) relocs.TryGetValue(slotVa, out val);
            if (val != 0) outp.Add(val);
        }

        return outp;
    }

    public static Dictionary<ulong, ulong> ReadRelativeRelocs(byte[] bin)
        => ReadRelativeRelocs(bin, 0, ulong.MaxValue);

    private static Dictionary<ulong, ulong> ReadRelativeRelocs(byte[] bin, ulong lo, ulong hi) {
        var map = new Dictionary<ulong, ulong>();
        if (!IsElf64Le(bin)) return map;
        try {
            ulong shoff = U64(bin, 0x28);
            int shentsize = U16(bin, 0x3A);
            int shnum = U16(bin, 0x3C);
            if (shentsize < 64 || shnum <= 0) return map;

            for (int i = 0; i < shnum; i++) {
                long h = (long)shoff + (long)i * shentsize;
                if (h < 0 || h + 64 > bin.Length) break;
                if (U32(bin, (int)h + 0x04) != ShtRela) continue;
                long off = (long)U64(bin, (int)h + 0x18);
                long sz = (long)U64(bin, (int)h + 0x20);
                for (long r = off; r + 24 <= off + sz && r + 24 <= bin.Length; r += 24) {
                    ulong roff = U64(bin, (int)r);
                    if (roff < lo || roff >= hi) continue;
                    if ((uint)(U64(bin, (int)r + 8) & 0xFFFFFFFF) != RAarch64Relative) continue;
                    map[roff] = U64(bin, (int)r + 16);
                }
            }
        } catch {
            return map;
        }

        return map;
    }

    public static Dictionary<ulong, ulong> ReadJumpSlots(byte[] bin) {
        var map = new Dictionary<ulong, ulong>();
        if (!IsElf64Le(bin)) return map;
        try {
            ulong shoff = U64(bin, 0x28);
            int shentsize = U16(bin, 0x3A);
            int shnum = U16(bin, 0x3C);
            if (shentsize < 64 || shnum <= 0) return map;

            for (int i = 0; i < shnum; i++) {
                long h = (long)shoff + (long)i * shentsize;
                if (h < 0 || h + 64 > bin.Length) break;
                if (U32(bin, (int)h + 0x04) != ShtRela) continue;
                uint link = U32(bin, (int)h + 0x28);
                long lh = (long)shoff + (long)link * shentsize;
                if (link >= (uint)shnum || lh < 0 || lh + 64 > bin.Length) continue;

                long symOff = (long)U64(bin, (int)lh + 0x18);
                long symEnt = Math.Max(Elf64SymSize, (long)U64(bin, (int)lh + 0x38));
                long off = (long)U64(bin, (int)h + 0x18);
                long sz = (long)U64(bin, (int)h + 0x20);
                for (long r = off; r + 24 <= off + sz && r + 24 <= bin.Length; r += 24) {
                    ulong info = U64(bin, (int)r + 8);
                    if ((uint)(info & 0xFFFFFFFF) != RAarch64JumpSlot) continue;
                    long e = symOff + (long)(info >> 32) * symEnt;
                    if (e < 0 || e + Elf64SymSize > bin.Length) continue;
                    ulong value = U64(bin, (int)e + 8);
                    if (value != 0) map[U64(bin, (int)r)] = value;
                }
            }
        } catch {
            return map;
        }

        return map;
    }

    internal static bool IsElf64Le(byte[]? b) =>
        b is { Length: >= 64 } && b[0] == 0x7F && b[1] == (byte)'E' && b[2] == (byte)'L' && b[3] == (byte)'F'
        && b[4] == 2 && b[5] == 1;

    internal static bool IsElf32Le(byte[]? b) =>
        b is { Length: >= 52 } && b[0] == 0x7F && b[1] == (byte)'E' && b[2] == (byte)'L' && b[3] == (byte)'F'
        && b[4] == 1 && b[5] == 1;

    private static ushort U16(byte[] b, int p) => (ushort)(b[p] | (b[p + 1] << 8));
    private static uint U32(byte[] b, int p) => (uint)(b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24));

    private static ulong U64(byte[] b, int p) {
        ulong v = 0;
        for (int i = 0; i < 8; i++) v |= (ulong)b[p + i] << (8 * i);
        return v;
    }

    private static string Cstr(byte[] b, long o) {
        if (o < 0 || o >= b.Length) return "";
        long end = o;
        while (end < b.Length && b[end] != 0) end++;
        return Encoding.ASCII.GetString(b, (int)o, (int)(end - o));
    }

    public readonly record struct LoadSegment(ulong VAddr, ulong VSize, long FileOff, long FileSize);
}
