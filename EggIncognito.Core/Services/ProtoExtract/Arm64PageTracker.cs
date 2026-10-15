using Gee.External.Capstone.Arm64;

namespace EggIncognito.Core.Services.ProtoExtract;

public sealed class Arm64PageTracker {
#pragma warning disable IDE0028
    private readonly Dictionary<string, ulong> _page = new(StringComparer.Ordinal);
#pragma warning restore IDE0028

    private readonly Dictionary<int, ulong> _gpr = [];
    private readonly Dictionary<int, (double Value, bool Wide)> _fp = [];

    public Arm64Image? Image { get; set; }

    public bool TryGet(string reg, out ulong va) => _page.TryGetValue(reg, out va);

    public ulong? Step(Arm64Instruction insn) {
        var ops = insn.Details?.Operands;
        if (ops is null) return null;

        string? kept = null;
        ulong? resolved = null;
        switch (insn.Id) {
            case Arm64InstructionId.ARM64_INS_ADRP:
                if (ops.Length == 2 && ops[0].Type == Arm64OperandType.Register
                                    && ops[1].Type == Arm64OperandType.Immediate && ops[0].Register is { } adrpRd) {
                    _page[adrpRd.Name] = (ulong)ops[1].Immediate;
                    kept = adrpRd.Name;
                }

                break;

            case Arm64InstructionId.ARM64_INS_ADD:
                if (ops.Length == 3 && ops[0].Register is { } addRd && ops[1].Register is { } addRn
                    && ops[2].Type == Arm64OperandType.Immediate &&
                    _page.TryGetValue(addRn.Name, out ulong addBase)) {
                    ulong full = addBase + (ulong)ops[2].Immediate;
                    _page[addRd.Name] = full;
                    kept = addRd.Name;
                    resolved = full;
                }

                break;
        }

        var written = insn.Details?.AllWrittenRegisters;
        if (written is not null) {
            foreach (var w in written) {
                if (w.Name is { } wn && wn != kept) _page.Remove(wn);
            }
        }

        return resolved;
    }

    public ulong? this[int reg] => _gpr.TryGetValue(reg, out ulong v) ? v : null;

    public void Reset() {
        _gpr.Clear();
        _fp.Clear();
    }

    public void Invalidate(int reg) {
        _gpr.Remove(reg);
        _fp.Remove(reg);
    }

    public bool TryValue(int reg, out ulong value) => _gpr.TryGetValue(reg, out value);

    public bool TryF64(int reg, out double value) {
        if (_fp.TryGetValue(reg, out var e) && e.Wide) {
            value = e.Value;
            return true;
        }

        value = 0;
        return false;
    }

    public bool TryF32(int reg, out float value) {
        if (_fp.TryGetValue(reg, out var e) && !e.Wide) {
            value = (float)e.Value;
            return true;
        }

        value = 0;
        return false;
    }

    public void Track(ulong va, uint word) {
        if (TryAdrpInstr(va, word)) return;
        if (TryAdrInstr(va, word)) return;
        if (TryAddImmInstr(word)) return;
        if (TrySubImmInstr(word)) return;
        if (TryMovWideInstr(word)) return;
        if (TryMovRegInstr(word)) return;
        if (TryOrrImmInstr(word)) return;
        if (TryLdrLiteralInstr(va, word)) return;
        if (TryLdrGprInstr(word)) return;
        if (TryLdrFpInstr(word)) return;
        if (TryFmovFromIntInstr(word)) return;
        InvalidateWritten(word);
    }

    private void InvalidateWritten(uint w) {
        int rd = (int)(w & 0x1F);
        uint op0 = (w >> 25) & 0xF;
        if ((op0 & 0xE) == 0xA) return;
        if ((op0 & 0x5) == 0x4) {
            if (((w >> 22) & 1) == 0) return;
            if (((w >> 26) & 1) != 0) _fp.Remove(rd);
            else _gpr.Remove(rd);
            return;
        }

        if ((op0 & 0x7) == 0x7) {
            if ((w & 0xFFFFFC00) is 0x1E260000 or 0x9E660000 or 0x9EAE0000) _gpr.Remove(rd);
            else _fp.Remove(rd);
            return;
        }

        _gpr.Remove(rd);
    }

    private bool TryAdrpInstr(ulong va, uint w) {
        if (!Arm64Bits.TryAdrp(va, w, out ulong page, out int rd)) return false;
        _gpr[rd] = page;
        _fp.Remove(rd);
        return true;
    }

    private bool TryAdrInstr(ulong va, uint w) {
        if ((w & 0x9F000000) != 0x10000000) return false;
        int rd = (int)(w & 0x1F);
        long immlo = (w >> 29) & 3;
        long immhi = (w >> 5) & 0x7FFFF;
        long imm = (immhi << 2) | immlo;
        if ((imm & (1L << 20)) != 0) imm -= 1L << 21;
        _gpr[rd] = (ulong)((long)va + imm);
        _fp.Remove(rd);
        return true;
    }

    private bool TryAddImmInstr(uint w) {
        if (!Arm64Bits.TryAddImm(w, out int rd, out int rn, out ulong imm)) return false;
        if (_gpr.TryGetValue(rn, out ulong baseVal)) _gpr[rd] = baseVal + imm;
        else _gpr.Remove(rd);
        _fp.Remove(rd);
        return true;
    }

    private bool TrySubImmInstr(uint w) {
        if ((w & 0x7F800000) != 0x51000000) return false;
        int rd = (int)(w & 0x1F);
        int rn = (int)((w >> 5) & 0x1F);
        int shift = (int)((w >> 22) & 1) * 12;
        ulong imm = ((w >> 10) & 0xFFF) << shift;
        if (_gpr.TryGetValue(rn, out ulong baseVal)) _gpr[rd] = baseVal - imm;
        else _gpr.Remove(rd);
        _fp.Remove(rd);
        return true;
    }

    private bool TryMovWideInstr(uint w) {
        if (!Arm64Bits.TryMovWide(w, out int rd, out ulong value, out var kind, out _)) return false;
        if (kind == Arm64Bits.MovKind.Movk) {
            ulong prior = _gpr.TryGetValue(rd, out ulong p) ? p : 0UL;
            _gpr[rd] = Arm64Bits.Merge(prior, w);
        } else {
            _gpr[rd] = value;
        }

        _fp.Remove(rd);
        return true;
    }

    private bool TryMovRegInstr(uint w) {
        if (!Arm64Bits.TryMovReg(w, out int rd, out int rm)) return false;
        if (_gpr.TryGetValue(rm, out ulong v)) _gpr[rd] = v;
        else _gpr.Remove(rd);
        _fp.Remove(rd);
        return true;
    }

    private bool TryOrrImmInstr(uint w) {
        if (!Arm64Bits.TryOrrImm(w, out int rd, out int rn, out ulong value, out _)) return false;
        if (rn != 31) return false;
        _gpr[rd] = value;
        _fp.Remove(rd);
        return true;
    }

    private bool TryLdrLiteralInstr(ulong va, uint w) {
        if ((w & 0x3B000000) != 0x18000000) return false;
        uint opc = (w >> 30) & 3;
        uint v = (w >> 26) & 1;
        int rt = (int)(w & 0x1F);
        if (v == 0 && opc == 3) return false;
        long imm19 = (w >> 5) & 0x7FFFF;
        if ((imm19 & 0x40000) != 0) imm19 -= 0x80000;
        ulong addr = (ulong)((long)va + (imm19 * 4));
        if (Image is null) {
            if (v == 0) _gpr.Remove(rt);
            else _fp.Remove(rt);
            return true;
        }

        if (v == 0) {
            switch (opc) {
                case 0:
                    if (Image.TryWord(addr, out uint w32)) {
                        _gpr[rt] = w32;
                        return true;
                    }

                    break;
                case 1:
                    if (Image.TryWord(addr, out uint lo) && Image.TryWord(addr + 4, out uint hi)) {
                        _gpr[rt] = lo | ((ulong)hi << 32);
                        return true;
                    }

                    break;
                case 2:
                    if (Image.TryWord(addr, out uint sw)) {
                        _gpr[rt] = unchecked((ulong)(long)(int)sw);
                        return true;
                    }

                    break;
            }

            _gpr.Remove(rt);
            return true;
        }

        switch (opc) {
            case 0:
                if (Image.TryF32(addr, out float f32)) {
                    _fp[rt] = (f32, false);
                    return true;
                }

                break;
            case 1:
                if (Image.TryF64(addr, out double f64)) {
                    _fp[rt] = (f64, true);
                    return true;
                }

                break;
        }

        _fp.Remove(rt);
        return true;
    }

    private bool TryLdrGprInstr(uint w) {
        if ((w & 0x3B000000) != 0x39000000 || ((w >> 26) & 1) != 0) return false;
        uint size = (w >> 30) & 3;
        uint opc = (w >> 22) & 3;
        int rt = (int)(w & 0x1F);
        if (opc != 1) {
            _gpr.Remove(rt);
            return true;
        }

        int rn = (int)((w >> 5) & 0x1F);
        int off = (int)(((w >> 10) & 0xFFF) << (int)size);
        if (!_gpr.TryGetValue(rn, out ulong baseVa) || Image is null) {
            _gpr.Remove(rt);
            return true;
        }

        ulong addr = baseVa + (ulong)off;
        switch (size) {
            case 2:
                if (Image.TryWord(addr, out uint w32)) {
                    _gpr[rt] = w32;
                    return true;
                }

                break;
            case 3:
                if (Image.TryPointer(addr, out ulong ptr)) {
                    _gpr[rt] = ptr;
                    return true;
                }

                break;
        }

        _gpr.Remove(rt);
        return true;
    }

    private bool TryLdrFpInstr(uint w) {
        if (!Arm64Bits.TryLoadFp(w, out var load)) return false;
        if (!_gpr.TryGetValue(load.Rn, out ulong baseVa) || Image is null) {
            _fp.Remove(load.Rt);
            return true;
        }

        ulong addr = baseVa + (ulong)load.Offset;
        if (load.Bytes == 4 && Image.TryF32(addr, out float f32)) {
            _fp[load.Rt] = (f32, false);
            return true;
        }

        if (load.Bytes == 8 && Image.TryF64(addr, out double f64)) {
            _fp[load.Rt] = (f64, true);
            return true;
        }

        _fp.Remove(load.Rt);
        return true;
    }

    private bool TryFmovFromIntInstr(uint w) {
        if (!Arm64Bits.TryFmovGprToFp(w, out int rd, out int rn, out bool is64)) return false;
        if (_gpr.TryGetValue(rn, out ulong bits))
            _fp[rd] = is64 ? (Arm64Bits.F64(bits), true) : (Arm64Bits.F32(bits), false);
        else
            _fp.Remove(rd);
        return true;
    }
}
