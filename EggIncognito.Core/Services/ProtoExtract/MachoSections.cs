using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class MachoSections {
    public static IReadOnlyList<Section> Read(byte[] bin) {
        var result = new List<Section>();
        if (bin is null || bin.Length < 32) return result;
        try {
            if (!MachoHeader.TryArm64Slice(bin, out int machoBase, out int sliceLen)) return result;
            foreach (var lc in MachoHeader.LoadCommands(bin.AsSpan(machoBase, sliceLen))) {
                if (lc.Cmd != MachoHeader.LcSegment64) continue;
                int o = machoBase + lc.Offset;
                string seg = Cstr16(bin, o + 8);
                uint nsects = U32(bin, o + 64);
                int sec = o + 72;
                for (uint s = 0; s < nsects; s++) {
                    if (sec + 80 > bin.Length) return result;
                    string sn = Cstr16(bin, sec);
                    ulong vmAddr = U64(bin, sec + 32);
                    ulong vmSize = U64(bin, sec + 40);
                    int fileOff = (int)U32(bin, sec + 48) + machoBase;
                    result.Add(new Section(seg, sn, vmAddr, vmSize, fileOff, machoBase));
                    sec += 80;
                }
            }
        } catch {
            return result;
        }

        return result;
    }

    public static bool TryVaToFileOffset(IReadOnlyList<Section> sections, ulong va, out int fileOff,
        out Section owner) {
        foreach (var s in sections) {
            if (s.VmSize == 0) continue;
            if (va >= s.VmAddr && va < s.VmAddr + s.VmSize) {
                long off = s.FileOff + (long)(va - s.VmAddr);
                if (off is >= 0 and <= int.MaxValue) {
                    fileOff = (int)off;
                    owner = s;
                    return true;
                }
            }
        }

        fileOff = 0;
        owner = default;
        return false;
    }

    public static Section? Find(IReadOnlyList<Section> sections, string segment, string name) {
        foreach (var s in sections) {
            if (s.Segment == segment && s.Name == name)
                return s;
        }

        return null;
    }

    private static uint U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

    private static ulong U64(byte[] b, int o) {
        ulong v = 0;
        for (int k = 0; k < 8; k++) v |= (ulong)b[o + k] << (k * 8);
        return v;
    }

    private static string Cstr16(byte[] b, int o) {
        int end = o;
        int max = Math.Min(o + 16, b.Length);
        while (end < max && b[end] != 0) end++;
        return Encoding.ASCII.GetString(b, o, end - o);
    }

    public readonly record struct Section(
        string Segment,
        string Name,
        ulong VmAddr,
        ulong VmSize,
        int FileOff,
        int MachoBase);
}
