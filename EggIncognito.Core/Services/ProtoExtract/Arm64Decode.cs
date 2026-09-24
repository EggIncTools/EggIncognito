using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class Arm64Decode {
    public static CapstoneArm64Disassembler CreateDisassembler(bool details = true) {
        var cs = CapstoneDisassembler.CreateArm64Disassembler(Arm64DisassembleMode.LittleEndian);
        cs.EnableInstructionDetails = details;
        return cs;
    }

    public static bool SliceFunction(byte[] bin, ulong startVa, ulong endVa, ulong textVmAddr, int textFileOff,
        out byte[] code, out ulong slide) {
        slide = textVmAddr - (ulong)textFileOff;
        code = [];
        long startFile = (long)startVa - (long)slide;
        long len = (long)endVa - (long)startVa;
        if (startFile < 0 || len <= 0 || startFile + len > bin.Length) return false;
        code = new byte[len];
        Array.Copy(bin, startFile, code, 0, (int)len);
        return true;
    }

    public static bool ReadPoolFloat(byte[] bin, ulong va, ulong slide, bool f64, out double value) {
        value = 0;
        long fileOff = (long)va - (long)slide;
        if (fileOff < 0) return false;
        if (f64 && fileOff + 8 <= bin.Length) {
            value = BitConverter.ToDouble(bin.AsSpan((int)fileOff));
            return true;
        }

        if (!f64 && fileOff + 4 <= bin.Length) {
            value = BitConverter.ToSingle(bin.AsSpan((int)fileOff));
            return true;
        }

        return false;
    }
}
