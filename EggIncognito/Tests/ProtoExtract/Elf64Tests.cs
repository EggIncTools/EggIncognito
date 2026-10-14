using System.Text;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class Elf64Tests {
    private static byte[] MiniElf(ulong textVAddr, long textOffset, long textSize) {
        byte[] shstrtab = Encoding.ASCII.GetBytes("\0.text\0.shstrtab\0");
        const int textNameOff = 1;
        const int strtabNameOff = 7;
        const int ehdr = 64, shentsize = 64;
        int shstrtabFileOff = ehdr;
        int shoff = shstrtabFileOff + shstrtab.Length;
        const int shnum = 3, shstrndx = 2;

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

        U64(0x28, (ulong)shoff);
        U16(0x3A, shentsize);
        U16(0x3C, shnum);
        U16(0x3E, shstrndx);
        Array.Copy(shstrtab, 0, buf, shstrtabFileOff, shstrtab.Length);

        int s1 = shoff + 1 * shentsize;
        U32(s1 + 0x00, textNameOff);
        U64(s1 + 0x08, 0x2);
        U64(s1 + 0x10, textVAddr);
        U64(s1 + 0x18, (ulong)textOffset);
        U64(s1 + 0x20, (ulong)textSize);

        int s2 = shoff + 2 * shentsize;
        U32(s2 + 0x00, strtabNameOff);
        U64(s2 + 0x18, (ulong)shstrtabFileOff);
        U64(s2 + 0x20, (ulong)shstrtab.Length);
        return buf;
    }

    [Fact]
    public void FindSection_Text_ReturnsAddrOffsetSize() {
        byte[] elf = MiniElf(0x1000, 0x400, 0x2000);
        var s = MachoSections.Find(ElfSections.Read(elf), "", ".text");
        Assert.NotNull(s);
        Assert.Equal(0x1000ul, s.Value.VmAddr);
        Assert.Equal(0x400, s.Value.FileOff);
        Assert.Equal(0x2000ul, s.Value.VmSize);
    }

    [Fact]
    public void FindSection_Missing_ReturnsNull() {
        byte[] elf = MiniElf(0x1000, 0x400, 0x2000);
        Assert.Null(MachoSections.Find(ElfSections.Read(elf), "", ".rodata"));
    }

    [Fact]
    public void FindSection_NotElf_ReturnsNull() =>
        Assert.Null(MachoSections.Find(ElfSections.Read([1, 2, 3, 4, 5, 6, 7, 8]), "", ".text"));
}
