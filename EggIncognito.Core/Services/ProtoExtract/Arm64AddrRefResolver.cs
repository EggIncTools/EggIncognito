using Gee.External.Capstone.Arm64;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class Arm64AddrRefResolver {
    public static IReadOnlyList<Ref> FindReferrers(byte[] bin, ulong targetVa) {
        var outp = new List<Ref>();
        if (bin is null || bin.Length < 64) return outp;
        if (!MachoText.TryFindText(bin, out int textFileOff, out int textSize, out ulong textVmAddr)) return outp;

        var starts = MachoFunctionStarts.Read(bin);
        if (starts.Count == 0) return outp;

        ulong slide = textVmAddr - (ulong)textFileOff;
        int textEnd = textFileOff + textSize;

        try {
            using var cs = Arm64Decode.CreateDisassembler();

            for (int i = 0; i < starts.Count; i++) {
                int fnStart = starts[i];
                if (fnStart < textFileOff || fnStart >= textEnd) continue;
                int fnEnd = i + 1 < starts.Count ? Math.Min(starts[i + 1], textEnd) : textEnd;
                int len = fnEnd - fnStart;
                if (len < 8 || fnStart + len > bin.Length) continue;

                int hits = CountAddrRefs(cs, bin, fnStart, len, slide, targetVa);
                if (hits > 0)
                    outp.Add(new Ref((ulong)fnStart + slide, targetVa, hits));
            }
        } catch (DllNotFoundException) {
            return outp;
        } catch {
            return outp;
        }

        outp.Sort((a, b) => b.HitCount.CompareTo(a.HitCount));
        return outp;
    }

    private static int CountAddrRefs(CapstoneArm64Disassembler cs, byte[] bin, int fileStart, int len, ulong slide,
        ulong targetVa) {
        byte[] code = new byte[len];
        Array.Copy(bin, fileStart, code, 0, len);
        ulong startVa = (ulong)fileStart + slide;

        var tracker = new Arm64PageTracker();
        int hits = 0;

        foreach (var insn in cs.Disassemble(code, (long)startVa)) {
            var ops = insn.Details?.Operands;
            if (ops is null) continue;

            if (insn.Id == Arm64InstructionId.ARM64_INS_LDR
                && ops.Length >= 2 && ops[0].Type == Arm64OperandType.Register
                && ops[^1].Type == Arm64OperandType.Memory && ops[^1].Memory?.Base is { } ldrBase
                && tracker.TryGet(ldrBase.Name, out ulong ldrPage)) {
                ulong slotVa = ldrPage + (ulong)ops[^1].Memory!.Displacement;
                long slotFile = (long)slotVa - (long)slide;
                if (slotFile >= 0 && slotFile + 8 <= bin.Length) {
                    ulong literal = BitConverter.ToUInt64(bin.AsSpan((int)slotFile));

                    if (literal == targetVa || (literal & 0x0000_FFFF_FFFF_FFFFUL) ==
                        (targetVa & 0x0000_FFFF_FFFF_FFFFUL)) {
                        hits++;
                    }
                }
            }

            if (tracker.Step(insn) is { } resolved && resolved == targetVa) hits++;
        }

        return hits;
    }

    public readonly record struct Ref(ulong FunctionVa, ulong ReferencedVa, int HitCount);
}
