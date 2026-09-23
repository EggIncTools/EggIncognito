using System.IO.Compression;
using System.Text;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class LibegincClientVersionTests {
    private const string SymbolName = "_ZNK14GameController20currentClientVersionEv";
    private const ulong TextVa = 0x1000;

    private static uint Movz(int wd, int imm16) => 0x52800000u | ((uint)(imm16 & 0xFFFF) << 5) | (uint)(wd & 0x1F);
    private const uint Ret = 0xD65F03C0;

    private static byte[] ConstReturn(int value) {
        uint[] w = [Movz(0, value), Ret];
        byte[] b = new byte[w.Length * 4];
        for (int i = 0; i < w.Length; i++) {
            for (int k = 0; k < 4; k++) {
                b[i * 4 + k] = (byte)(w[i] >> (8 * k));
            }
        }

        return b;
    }

    private static byte[] SoWithSymbol(byte[] textBytes, string symbol, ulong textVa) {
        byte[] shstr = Encoding.ASCII.GetBytes("\0.text\0.symtab\0.strtab\0.shstrtab\0");
        int nText = 1, nSymtab = 7, nStrtab = 15, nShstr = 23;
        byte[] strtab = Encoding.ASCII.GetBytes("\0" + symbol + "\0");
        int symNameOff = 1;

        const int ehdr = 64, shentsize = 64, shnum = 5, shstrndx = 4;
        int shstrOff = ehdr;
        int strtabOff = shstrOff + shstr.Length;
        int symtabOff = strtabOff + strtab.Length;
        const int symEntSize = 24;
        int symtabSize = symEntSize * 2;
        int textOff = symtabOff + symtabSize;
        int shoff = textOff + textBytes.Length;
        byte[] buf = new byte[shoff + shnum * shentsize];

        buf[0] = 0x7F;
        buf[1] = (byte)'E';
        buf[2] = (byte)'L';
        buf[3] = (byte)'F';
        buf[4] = 2;
        buf[5] = 1;

        void U16(int p, ushort v) {
            buf[p] = (byte)v;
            buf[p + 1] = (byte)(v >> 8);
        }

        void U32(int p, uint v) {
            for (int i = 0; i < 4; i++) buf[p + i] = (byte)(v >> (8 * i));
        }

        void U64(int p, ulong v) {
            for (int i = 0; i < 8; i++) buf[p + i] = (byte)(v >> (8 * i));
        }

        U16(0x10, 3);
        U16(0x12, 0xB7);
        U64(0x28, (ulong)shoff);
        U16(0x3A, shentsize);
        U16(0x3C, shnum);
        U16(0x3E, shstrndx);

        Array.Copy(shstr, 0, buf, shstrOff, shstr.Length);
        Array.Copy(strtab, 0, buf, strtabOff, strtab.Length);
        Array.Copy(textBytes, 0, buf, textOff, textBytes.Length);

        int sym1 = symtabOff + symEntSize;
        U32(sym1 + 0x00, (uint)symNameOff);
        buf[sym1 + 0x04] = 0x12;
        U16(sym1 + 0x06, 1);
        U64(sym1 + 0x08, textVa);
        U64(sym1 + 0x10, (ulong)textBytes.Length);

        void Shdr(int idx, uint name, uint type, ulong addr, ulong off, ulong size, uint link, ulong entsize) {
            int h = shoff + idx * shentsize;
            U32(h + 0x00, name);
            U32(h + 0x04, type);
            U64(h + 0x08, type == 1 ? 0x2UL : 0);
            U64(h + 0x10, addr);
            U64(h + 0x18, off);
            U64(h + 0x20, size);
            U32(h + 0x28, link);
            U64(h + 0x38, entsize);
        }

        Shdr(1, (uint)nText, 1, textVa, (ulong)textOff, (ulong)textBytes.Length, 0, 0);
        Shdr(2, (uint)nSymtab, 2, 0, (ulong)symtabOff, (ulong)symtabSize, 3, symEntSize);
        Shdr(3, (uint)nStrtab, 3, 0, (ulong)strtabOff, (ulong)strtab.Length, 0, 0);
        Shdr(4, (uint)nShstr, 3, 0, (ulong)shstrOff, (ulong)shstr.Length, 0, 0);
        return buf;
    }

    private static byte[] Raw(params uint[] w) {
        byte[] b = new byte[w.Length * 4];
        for (int i = 0; i < w.Length; i++) {
            for (int k = 0; k < 4; k++) b[i * 4 + k] = (byte)(w[i] >> (8 * k));
        }

        return b;
    }

    private static byte[] Elf32ThumbConstReturn(int imm, string symbol, uint textVa) {
        byte[] text = [(byte)(imm & 0xFF), 0x20, 0x70, 0x47];

        byte[] shstr = Encoding.ASCII.GetBytes("\0.text\0.dynsym\0.dynstr\0.shstrtab\0");
        int nText = 1, nDynsym = 7, nDynstr = 15, nShstr = 23;
        byte[] dynstr = Encoding.ASCII.GetBytes("\0" + symbol + "\0");

        const int ehdr = 52, phentsize = 32, phnum = 1, shentsize = 40, shnum = 5, shstrndx = 4, symEnt = 16;
        int phOff = ehdr;
        int shstrOff = phOff + phentsize * phnum;
        int dynstrOff = shstrOff + shstr.Length;
        int dynsymOff = dynstrOff + dynstr.Length;
        int dynsymSize = symEnt * 2;
        int textOff = (int)textVa;
        int fileEnd = Math.Max(dynsymOff + dynsymSize, textOff + text.Length);
        int shoff = fileEnd;
        byte[] buf = new byte[shoff + shnum * shentsize];

        buf[0] = 0x7F;
        buf[1] = (byte)'E';
        buf[2] = (byte)'L';
        buf[3] = (byte)'F';
        buf[4] = 1;
        buf[5] = 1;

        void U16(int p, ushort v) {
            buf[p] = (byte)v;
            buf[p + 1] = (byte)(v >> 8);
        }

        void U32(int p, uint v) {
            for (int i = 0; i < 4; i++) buf[p + i] = (byte)(v >> (8 * i));
        }

        U16(0x10, 3);
        U16(0x12, 40);
        U32(0x1C, (uint)phOff);
        U16(0x2A, phentsize);
        U16(0x2C, phnum);
        U32(0x20, (uint)shoff);
        U16(0x2E, shentsize);
        U16(0x30, shnum);
        U16(0x32, shstrndx);

        U32(phOff + 0x00, 1);
        U32(phOff + 0x04, 0);
        U32(phOff + 0x08, 0);
        U32(phOff + 0x10, (uint)buf.Length);
        U32(phOff + 0x14, (uint)buf.Length);

        Array.Copy(shstr, 0, buf, shstrOff, shstr.Length);
        Array.Copy(dynstr, 0, buf, dynstrOff, dynstr.Length);
        Array.Copy(text, 0, buf, textOff, text.Length);

        int sym1 = dynsymOff + symEnt;
        U32(sym1 + 0x00, 1);
        U32(sym1 + 0x04, textVa | 1);
        U32(sym1 + 0x08, (uint)text.Length);
        buf[sym1 + 0x0C] = 0x12;
        U16(sym1 + 0x0E, 1);

        void Shdr(int idx, uint name, uint type, uint off, uint size, uint link, uint entsize) {
            int h = shoff + idx * shentsize;
            U32(h + 0x00, name);
            U32(h + 0x04, type);
            U32(h + 0x10, off);
            U32(h + 0x14, size);
            U32(h + 0x18, link);
            U32(h + 0x24, entsize);
        }

        Shdr(1, (uint)nText, 1, textVa, (uint)text.Length, 0, 0);
        Shdr(2, (uint)nDynsym, 11, (uint)dynsymOff, (uint)dynsymSize, 3, symEnt);
        Shdr(3, (uint)nDynstr, 3, (uint)dynstrOff, (uint)dynstr.Length, 0, 0);
        Shdr(4, (uint)nShstr, 3, (uint)shstrOff, (uint)shstr.Length, 0, 0);
        return buf;
    }

    [Fact]
    public void ReadFromBinary_SymbolConstReturn_DecodesImmediate() =>
        Assert.Equal(74, LibegincClientVersion.ReadFromBinary(SoWithSymbol(ConstReturn(74), SymbolName, TextVa)));

    [Fact]
    public void ReadFromBinary_Arm64OrrBitmask_DecodesMovAlias() =>
        Assert.Equal(24, LibegincClientVersion.ReadFromBinary(SoWithSymbol(Raw(0x321D07E0, Ret), SymbolName, TextVa)));

    [Fact]
    public void ReadFromBinary_Elf32ThumbMovsBx_DecodesImmediate() =>
        Assert.Equal(16, LibegincClientVersion.ReadFromBinary(Elf32ThumbConstReturn(16, SymbolName, 0x2000)));

    [Fact]
    public void ReadFromBinary_Elf32Thumb_UnrelatedSymbol_ReturnsNull() =>
        Assert.Null(LibegincClientVersion.ReadFromBinary(Elf32ThumbConstReturn(16, "_Z7unrelatedv", 0x2000)));

    [Fact]
    public void Read_ApkZip_PullsSoEntryAndDecodes() {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
            var e = zip.CreateEntry("lib/arm64-v8a/libegginc.so");
            using var es = e.Open();
            byte[] so = SoWithSymbol(ConstReturn(72), SymbolName, TextVa);
            es.Write(so, 0, so.Length);
        }

        Assert.Equal(72, LibegincClientVersion.Read(ms.ToArray()));
    }

    [Fact]
    public void ReadFromBinary_NoSymbol_ReturnsNull() =>
        Assert.Null(LibegincClientVersion.ReadFromBinary(SoWithSymbol(ConstReturn(72), "_Z7unrelatedv", TextVa)));

    [Fact]
    public void ReadFromBinary_RealAndroidBinary_MatchesRinfo() {
        string path = Path.Combine(RepoRoot(), "EggIncognito", "captures", "egginc-android-1.37.so");
        if (!File.Exists(path)) return;
        Assert.Equal(75, LibegincClientVersion.ReadFromBinary(File.ReadAllBytes(path)));
    }

    [Theory]
    [InlineData("egginc-1.7.7", 15)]
    [InlineData("egginc", 72)]
    public void ReadFromBinary_RealIosBinary_MatchesCompiledConstant(string name, int expected) {
        string path = Path.Combine(RepoRoot(), "EggIncognito", "captures", name);
        if (!File.Exists(path)) return;
        Assert.Equal(expected, LibegincClientVersion.ReadFromBinary(File.ReadAllBytes(path)));
    }

    private static string RepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EggIncognito.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
