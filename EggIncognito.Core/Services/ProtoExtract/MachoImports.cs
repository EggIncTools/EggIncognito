using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class MachoImports {
    private const uint SectionTypeMask = 0xFF;
    private const uint SLazySymbolPointers = 0x7;
    private const uint SNonLazySymbolPointers = 0x6;
    private const uint IndirectSymbolLocal = 0x80000000;
    private const uint IndirectSymbolAbs = 0x40000000;
    private const int NlistSize = 16;

    public static Dictionary<ulong, string> ReadPointerSlotNames(byte[] bin) {
        var map = new Dictionary<ulong, string>();
        if (bin is null || bin.Length < 32) return map;
        try {
            if (!MachoHeader.TryArm64Slice(bin, out int b, out int sliceLen)) return map;

            var pointerSections = new List<(ulong Addr, ulong Size, uint Reserved1)>();
            uint symoff = 0, stroff = 0, strsize = 0, indirectOff = 0, nIndirect = 0;
            foreach (var lc in MachoHeader.LoadCommands(bin.AsSpan(b, sliceLen))) {
                int o = b + lc.Offset;
                if (lc.Cmd == MachoHeader.LcSegment64) {
                    uint nsects = U32(bin, o + 64);
                    int sec = o + 72;
                    for (uint s = 0; s < nsects; s++) {
                        if (sec + 80 > bin.Length) return map;
                        uint type = U32(bin, sec + 64) & SectionTypeMask;
                        if (type is SLazySymbolPointers or SNonLazySymbolPointers)
                            pointerSections.Add((U64(bin, sec + 32), U64(bin, sec + 40), U32(bin, sec + 68)));
                        sec += 80;
                    }
                } else if (lc.Cmd == MachoHeader.LcSymtab) {
                    symoff = U32(bin, o + 8) + (uint)b;
                    stroff = U32(bin, o + 16) + (uint)b;
                    strsize = U32(bin, o + 20);
                } else if (lc.Cmd == MachoHeader.LcDysymtab) {
                    indirectOff = U32(bin, o + 56) + (uint)b;
                    nIndirect = U32(bin, o + 60);
                }
            }

            if (symoff == 0 || indirectOff == 0) return map;

            foreach (var (addr, size, first) in pointerSections) {
                for (ulong k = 0; k < size / 8; k++) {
                    ulong idx = first + k;
                    if (idx >= nIndirect) break;
                    uint sym = U32(bin, (int)(indirectOff + idx * 4));
                    if ((sym & (IndirectSymbolLocal | IndirectSymbolAbs)) != 0) continue;
                    long n = symoff + (long)sym * NlistSize;
                    if (n < 0 || n + NlistSize > bin.Length) continue;
                    uint strx = U32(bin, (int)n);
                    if (strx >= strsize) continue;
                    long s = stroff + strx;
                    int end = Array.IndexOf(bin, (byte)0, (int)s);
                    if (end <= s) continue;
                    string name = Encoding.ASCII.GetString(bin, (int)s, end - (int)s);
                    map[addr + k * 8] = name.StartsWith('_') ? name[1..] : name;
                }
            }
        } catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException
                                         or OverflowException) {
            return map;
        }

        return map;
    }

    private static uint U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

    private static ulong U64(byte[] b, int o) {
        ulong v = 0;
        for (int k = 0; k < 8; k++) v |= (ulong)b[o + k] << (k * 8);
        return v;
    }
}
