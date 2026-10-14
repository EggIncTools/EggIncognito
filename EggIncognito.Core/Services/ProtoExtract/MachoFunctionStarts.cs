using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class MachoFunctionStarts {
    public static IReadOnlyList<int> Read(byte[] bin) {
        var outp = new List<int>();
        if (bin is null || bin.Length < 32) return outp;
        try {
            if (!MachoHeader.TryArm64Slice(bin, out int b, out int sliceLen)) return outp;

            int dataoff = 0, datasize = 0;
            uint textFileOff = 0;
            bool haveText = false;

            foreach (var lc in MachoHeader.LoadCommands(bin.AsSpan(b, sliceLen))) {
                int o = b + lc.Offset;
                if (lc.Cmd == MachoHeader.LcFunctionStarts) {
                    dataoff = (int)U32(bin, o + 8) + b;
                    datasize = (int)U32(bin, o + 12);
                } else if (lc.Cmd == MachoHeader.LcSegment64 && Cstr16(bin, o + 8) == "__TEXT") {
                    textFileOff = U32(bin, o + 40);
                    haveText = true;
                }
            }

            if (datasize == 0 || dataoff <= 0 || dataoff + datasize > bin.Length || !haveText) return outp;

            long off = textFileOff;
            int p = dataoff;
            int end = dataoff + datasize;
            while (p < end) {
                ulong delta = ReadUleb(bin, ref p, end);
                if (delta == 0) break;
                off += (long)delta;
                if (off < 0 || off >= bin.Length) break;
                outp.Add((int)off);
            }
        } catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException
                                         or OverflowException) {
            return outp;
        }

        return outp;
    }

    public static bool TryEnclosingStart(IBinaryImage image, ulong targetVa, out ulong startVa, out ulong endVa) {
        startVa = 0;
        endVa = 0;
        if (!image.TryFindText(out int textFileOff, out int textSize, out ulong textVm)) return false;
        var starts = Read(image.Bytes);
        if (starts.Count == 0) return false;
        ulong slide = textVm - (ulong)textFileOff;
        ulong textEndVa = textVm + (ulong)textSize;

        ulong best = 0;
        bool have = false;
        ulong next = textEndVa;
        for (int i = 0; i < starts.Count; i++) {
            ulong va = (ulong)starts[i] + slide;
            if (va <= targetVa && va >= best) {
                best = va;
                have = true;
                next = i + 1 < starts.Count ? (ulong)starts[i + 1] + slide : textEndVa;
            }
        }

        if (!have) return false;
        startVa = best;
        endVa = next;
        return true;
    }

    private static ulong ReadUleb(byte[] b, ref int p, int end) {
        ulong result = 0;
        int shift = 0;
        while (p < end) {
            byte by = b[p++];
            result |= (ulong)(by & 0x7F) << shift;
            if ((by & 0x80) == 0) break;
            shift += 7;
            if (shift > 63) break;
        }

        return result;
    }

    private static uint U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

    private static string Cstr16(byte[] b, int o) {
        int end = o, max = Math.Min(o + 16, b.Length);
        while (end < max && b[end] != 0) end++;
        return Encoding.ASCII.GetString(b, o, end - o);
    }
}
