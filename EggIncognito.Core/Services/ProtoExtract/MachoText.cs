using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class MachoText {
    public static bool TryFindText(byte[] bin, out int fileOff, out int size, out ulong vmAddr) {
        fileOff = 0;
        size = 0;
        vmAddr = 0;
        if (bin is null || bin.Length < 32) return false;
        try {
            if (!MachoHeader.TryArm64Slice(bin, out int machoBase, out int sliceLen)) return false;
            foreach (var lc in MachoHeader.LoadCommands(bin.AsSpan(machoBase, sliceLen))) {
                if (lc.Cmd != MachoHeader.LcSegment64) continue;
                int o = machoBase + lc.Offset;
                string seg = Cstr16(bin, o + 8);
                if (seg != "__TEXT") continue;
                uint nsects = U32(bin, o + 64);
                int sec = o + 72;
                for (uint s = 0; s < nsects; s++) {
                    if (sec + 80 > bin.Length) return false;
                    string sn = Cstr16(bin, sec);
                    if (sn == "__text") {
                        vmAddr = U64(bin, sec + 32);
                        size = (int)U64(bin, sec + 40);
                        fileOff = (int)U32(bin, sec + 48) + machoBase;
                        return fileOff >= 0 && size >= 0 && (long)fileOff + size <= bin.Length && size > 0;
                    }

                    sec += 80;
                }
            }

            return false;
        } catch {
            return false;
        }
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
}
