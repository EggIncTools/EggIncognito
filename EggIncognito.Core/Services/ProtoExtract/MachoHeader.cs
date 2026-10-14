using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public sealed record MachoSegment(string Name, ulong VmAddr, ulong VmSize, ulong FileOff, ulong FileSize, IReadOnlyList<MachoSection> Sections);

public sealed record MachoSection(string SegName, string SectName, ulong Addr, ulong Size, uint Offset);

public sealed record LoadCommand(uint Cmd, int Offset, int Size);

public static class MachoHeader {
    public const uint MhMagic64 = 0xFEEDFACF;
    public const uint FatMagic = 0xCAFEBABE;
    public const uint CpuArm64 = 0x0100000C;
    public const uint LcSegment64 = 0x19;
    public const uint LcSymtab = 0x2;
    public const uint LcFunctionStarts = 0x26;
    public const uint LcDysymtab = 0xB;

    public static bool IsMacho(ReadOnlySpan<byte> bytes) {
        if (bytes.Length < 8) return false;
        return U32(bytes, 0) == MhMagic64 || U32be(bytes, 0) == FatMagic;
    }

    public static bool TryArm64Slice(ReadOnlySpan<byte> file, out int offset, out int length) {
        offset = 0;
        length = 0;
        if (file.Length < 32) return false;
        if (U32(file, 0) == MhMagic64) {
            length = file.Length;
            return true;
        }

        if (U32be(file, 0) != FatMagic) return false;
        uint nfat = U32be(file, 4);
        int e = 8;
        for (uint i = 0; i < nfat; i++) {
            if (e + 20 > file.Length) return false;
            uint cputype = U32be(file, e);
            uint off = U32be(file, e + 8);
            uint size = U32be(file, e + 12);
            if (cputype == CpuArm64) {
                if (off > int.MaxValue || size > int.MaxValue) return false;
                if ((long)off + size > file.Length) return false;
                if (off + 32 > file.Length || U32(file, (int)off) != MhMagic64) return false;
                offset = (int)off;
                length = (int)size;
                return true;
            }

            e += 20;
        }

        return false;
    }

    public static IReadOnlyList<LoadCommand> LoadCommands(ReadOnlySpan<byte> slice) {
        var result = new List<LoadCommand>();
        if (slice.Length < 32 || U32(slice, 0) != MhMagic64) return result;
        uint ncmds = U32(slice, 16);
        int lc = 32;
        for (uint c = 0; c < ncmds; c++) {
            if (lc + 8 > slice.Length) return result;
            uint cmd = U32(slice, lc);
            uint cmdsize = U32(slice, lc + 4);
            if (cmdsize < 8 || lc + cmdsize > slice.Length) return result;
            result.Add(new LoadCommand(cmd, lc, (int)cmdsize));
            lc += (int)cmdsize;
        }

        return result;
    }

    public static IReadOnlyList<MachoSegment> Segments(ReadOnlySpan<byte> slice) {
        var result = new List<MachoSegment>();
        foreach (var lc in LoadCommands(slice)) {
            if (lc.Cmd != LcSegment64 || lc.Size < 72) continue;
            int o = lc.Offset;
            string seg = Cstr16(slice, o + 8);
            ulong vmAddr = U64(slice, o + 24);
            ulong vmSize = U64(slice, o + 32);
            ulong fileOff = U64(slice, o + 40);
            ulong fileSize = U64(slice, o + 48);
            uint nsects = U32(slice, o + 64);
            var sections = new List<MachoSection>();
            int sec = o + 72;
            for (uint s = 0; s < nsects; s++) {
                if (sec + 80 > slice.Length || sec + 80 > o + lc.Size) break;
                string sn = Cstr16(slice, sec);
                string sg = Cstr16(slice, sec + 16);
                ulong addr = U64(slice, sec + 32);
                ulong size = U64(slice, sec + 40);
                uint off = U32(slice, sec + 48);
                sections.Add(new MachoSection(sg, sn, addr, size, off));
                sec += 80;
            }

            result.Add(new MachoSegment(seg, vmAddr, vmSize, fileOff, fileSize, sections));
        }

        return result;
    }

    private static uint U32(ReadOnlySpan<byte> b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    private static uint U32be(ReadOnlySpan<byte> b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

    private static ulong U64(ReadOnlySpan<byte> b, int o) {
        ulong v = 0;
        for (int k = 0; k < 8; k++) v |= (ulong)b[o + k] << (k * 8);
        return v;
    }

    private static string Cstr16(ReadOnlySpan<byte> b, int o) {
        int end = o;
        int max = Math.Min(o + 16, b.Length);
        while (end < max && b[end] != 0) end++;
        return Encoding.ASCII.GetString(b[o..end]);
    }
}
