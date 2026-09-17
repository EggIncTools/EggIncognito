using Gee.External.Capstone.Arm64;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class MachoArm64Disassembler {
    public static AnalysisResult Analyze(byte[] bin, ulong startVa, ulong endVa, ulong textVmAddr, int textFileOff) {
        var floats = new List<FloatConst>();
        var calls = new List<ulong>();

        if (!Arm64Decode.SliceFunction(bin, startVa, endVa, textVmAddr, textFileOff, out byte[] code,
                out ulong slide)) {
            return new AnalysisResult(floats, calls);
        }

        using var cs = Arm64Decode.CreateDisassembler();

        var tracker = new Arm64PageTracker();
        foreach (var insn in cs.Disassemble(code, (long)startVa)) {
            var ops = insn.Details?.Operands;
            if (ops is null) continue;

            switch (insn.Id) {
                case Arm64InstructionId.ARM64_INS_LDR:
                case Arm64InstructionId.ARM64_INS_LDUR:
                    if (ops.Length >= 2 && ops[0].Type == Arm64OperandType.Register
                                        && ops[^1].Type == Arm64OperandType.Memory && ops[0].Register is { } rt
                                        && ops[^1].Memory?.Base is { } memBase &&
                                        tracker.TryGet(memBase.Name, out ulong pv)) {
                        ulong va = pv + (ulong)ops[^1].Memory!.Displacement;
                        long fileOff = (long)va - (long)slide;
                        string name = rt.Name ?? "";

                        if (name.StartsWith('q') && fileOff >= 0 && fileOff + 16 <= bin.Length) {
                            for (int lane = 0; lane < 4; lane++) {
                                floats.Add(new FloatConst(va + (ulong)(lane * 4),
                                    BitConverter.ToSingle(bin, (int)fileOff + lane * 4), false));
                            }
                        } else if (name.StartsWith('d') && fileOff >= 0 && fileOff + 8 <= bin.Length) {
                            floats.Add(new FloatConst(va, BitConverter.ToDouble(bin, (int)fileOff), true));
                        } else if (name.StartsWith('s') && fileOff >= 0 && fileOff + 4 <= bin.Length) {
                            floats.Add(new FloatConst(va, BitConverter.ToSingle(bin, (int)fileOff), false));
                        }
                    }

                    break;

                case Arm64InstructionId.ARM64_INS_FMOV:
                    if (ops.Length == 2 && ops[0].Type == Arm64OperandType.Register
                                        && ops[1].Type == Arm64OperandType.FloatingPoint &&
                                        ops[0].Register is { } fmovRd) {
                        bool f64 = fmovRd.Name?.StartsWith('d') == true;
                        floats.Add(new FloatConst((ulong)insn.Address, ops[1].FloatingPoint, f64));
                    }

                    break;

                case Arm64InstructionId.ARM64_INS_MOVI:
                    if (ops.Length == 2 && ops[1].Type == Arm64OperandType.Immediate && ops[1].Immediate == 0)
                        floats.Add(new FloatConst((ulong)insn.Address, 0.0, false));
                    break;

                case Arm64InstructionId.ARM64_INS_BL:
                    if (ops.Length == 1 && ops[0].Type == Arm64OperandType.Immediate)
                        calls.Add((ulong)ops[0].Immediate);
                    break;
            }

            tracker.Step(insn);
        }

        return new AnalysisResult(floats, calls);
    }

    public readonly record struct FloatConst(ulong Va, double Value, bool IsF64);

    public readonly record struct AnalysisResult(IReadOnlyList<FloatConst> Floats, IReadOnlyList<ulong> CallTargets);
}
