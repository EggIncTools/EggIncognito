namespace EggIncognito.Core.Services.ProtoExtract;

public sealed class Arm64Image(byte[] bin, IBinaryImage img) {
    public bool TryByte(ulong va, out byte value) {
        value = 0;
        if (!img.TryVaToFileOffset(va, out int off, out _) || off < 0 || off >= bin.Length) return false;
        value = bin[off];
        return true;
    }

    public bool TryWord(ulong va, out uint word) {
        word = 0;
        if (!img.TryVaToFileOffset(va, out int off, out _) || off < 0 || off + 4 > bin.Length) return false;
        word = BitConverter.ToUInt32(bin, off);
        return true;
    }

    public bool TryF32(ulong va, out float value) {
        value = 0f;
        if (!img.TryVaToFileOffset(va, out int off, out _) || off < 0 || off + 4 > bin.Length) return false;
        value = BitConverter.ToSingle(bin, off);
        return float.IsFinite(value);
    }

    public bool TryF64(ulong va, out double value) {
        value = 0d;
        if (!img.TryVaToFileOffset(va, out int off, out _) || off < 0 || off + 8 > bin.Length) return false;
        value = BitConverter.ToDouble(bin, off);
        return double.IsFinite(value);
    }

    public bool TryF32Table(ulong va, int count, out float[] values) {
        var buffer = new float[count];
        for (int i = 0; i < count; i++) {
            if (!TryF32(va + (ulong)(4 * i), out buffer[i])) {
                values = [];
                return false;
            }
        }

        values = buffer;
        return true;
    }

    public bool TryPageRef(ulong adrpVa, out ulong target) {
        target = 0;
        if (!TryWord(adrpVa, out uint adrp) || !Arm64Bits.TryAdrp(adrpVa, adrp, out ulong page, out int rd))
            return false;
        if (!TryWord(adrpVa + 4, out uint next)) return false;
        if (Arm64Bits.TryLoadFp(next, out var load) && load.Rn == rd) {
            target = page + (ulong)load.Offset;
            return true;
        }

        if (!Arm64Bits.TryAddImm(next, out _, out int addRn, out ulong imm) || addRn != rd) return false;
        target = page + imm;
        return true;
    }
}

public static class Arm64Bits {
    public enum MovKind {
        Movz,
        Movn,
        Movk
    }

    public readonly record struct StoreInfo(bool Fp, int Size, int Rt, int Rn, int Offset);

    public readonly record struct LoadInfo(int Rt, int Rn, int Offset, int Bytes);

    public readonly record struct SturInfo(int Rt, int Rn, int Offset, int Bytes);

    public readonly record struct AddShiftedInfo(int Rd, int Rn, int Rm, int ShiftKind, int Amount);

    public static float F32(ulong bits) => BitConverter.Int32BitsToSingle(unchecked((int)(uint)bits));

    public static double F64(ulong bits) => BitConverter.Int64BitsToDouble(unchecked((long)bits));

    public static bool TryMovWide(uint w, out int rd, out ulong value, out MovKind kind, out bool is64) {
        rd = (int)(w & 0x1F);
        is64 = (w >> 31) != 0;
        int hw = (int)((w >> 21) & 3);
        ulong imm = (w >> 5) & 0xFFFF;
        ulong shifted = imm << (16 * hw);
        switch (w & 0x7F800000) {
            case 0x52800000:
                kind = MovKind.Movz;
                value = shifted;
                break;
            case 0x12800000:
                kind = MovKind.Movn;
                value = ~shifted;
                break;
            case 0x72800000:
                kind = MovKind.Movk;
                value = shifted;
                break;
            default:
                kind = MovKind.Movz;
                value = 0;
                return false;
        }

        if (!is64) {
            if (hw > 1) return false;
            value &= 0xFFFFFFFF;
        }

        return true;
    }

    public static ulong Merge(ulong prior, uint movkWord) {
        int hw = (int)((movkWord >> 21) & 3);
        ulong imm = (movkWord >> 5) & 0xFFFF;
        ulong mask = 0xFFFFUL << (16 * hw);
        ulong merged = (prior & ~mask) | (imm << (16 * hw));
        return (movkWord >> 31) == 0 ? merged & 0xFFFFFFFF : merged;
    }

    public static bool TryOrrImm(uint w, out int rd, out int rn, out ulong value, out bool is64) {
        rd = (int)(w & 0x1F);
        rn = (int)((w >> 5) & 0x1F);
        is64 = (w >> 31) != 0;
        value = 0;
        if ((w & 0x7F800000) != 0x32000000) return false;
        uint n = (w >> 22) & 1;
        uint immr = (w >> 16) & 0x3F;
        uint imms = (w >> 10) & 0x3F;
        return TryBitMask(n, imms, immr, is64, out value);
    }

    public static bool TryConst(Arm64Image im, ulong va, out ulong bits, out bool is64) {
        bits = 0;
        is64 = false;
        if (!im.TryWord(va, out uint w)) return false;

        int rd;
        if (TryMovWide(w, out int movRd, out ulong movValue, out var kind, out bool movIs64)
            && kind != MovKind.Movk) {
            rd = movRd;
            bits = movValue;
            is64 = movIs64;
        } else if (TryOrrImm(w, out int orrRd, out int orrRn, out ulong orrValue, out bool orrIs64)
                   && orrRn == 31) {
            rd = orrRd;
            bits = orrValue;
            is64 = orrIs64;
        } else {
            return false;
        }

        ulong cursor = va + 4;
        for (int i = 0; i < 3; i++) {
            if (!im.TryWord(cursor, out uint next)) break;
            if ((next & 0x7F800000) != 0x72800000) break;
            if ((int)(next & 0x1F) != rd) break;
            bool sf = (next >> 31) != 0;
            if (sf != is64) break;
            bits = Merge(bits, next);
            cursor += 4;
        }

        return true;
    }

    public static bool TryFmovImm(Arm64Image im, ulong va, out double value, out bool f64) {
        value = 0d;
        f64 = false;
        return im.TryWord(va, out uint w) && TryFmovImm(w, out value, out f64, out _);
    }

    public static bool TryFmovImm(uint w, out double value, out bool f64, out int rd) {
        value = 0d;
        f64 = false;
        rd = (int)(w & 0x1F);
        if ((w & 0xFF201C00) != 0x1E201000) return false;
        uint type = (w >> 22) & 3;
        if (type > 1) return false;
        f64 = type == 1;
        value = Expand((w >> 13) & 0xFF, f64);
        return true;
    }

    public static bool TryFmovGprToFp(uint w, out int rd, out int rn, out bool is64) {
        rd = (int)(w & 0x1F);
        rn = (int)((w >> 5) & 0x1F);
        is64 = (w >> 31) != 0;
        uint masked = w & 0xFFFFFC00;
        return masked is 0x1E270000 or 0x9E670000;
    }

    public static bool IsFmovZeroToFp(uint w) =>
        TryFmovGprToFp(w, out _, out int rn, out _) && rn == 31;

    public static bool IsFcsel(uint w) => (w & 0xFF200C00) == 0x1E200C00;

    public static bool TryCmpImm(Arm64Image im, ulong va, out ulong imm, out int rn, out bool is64) {
        imm = 0;
        rn = 0;
        is64 = false;
        return im.TryWord(va, out uint w) && TryCmpImm(w, out imm, out rn, out is64);
    }

    public static bool TryCmpImm(uint w, out ulong imm, out int rn, out bool is64) {
        imm = 0;
        rn = (int)((w >> 5) & 0x1F);
        is64 = (w >> 31) != 0;
        if ((w & 0x7F800000) != 0x71000000 || (w & 0x1F) != 0x1F) return false;
        int shift = (int)((w >> 22) & 1) * 12;
        imm = ((w >> 10) & 0xFFF) << shift;
        return true;
    }

    public static bool TryStore(uint w, out StoreInfo store) {
        store = default;
        if ((w & 0x3B000000) != 0x39000000) return false;
        if (((w >> 22) & 3) != 0) return false;
        int size = (int)((w >> 30) & 3);
        store = new StoreInfo(((w >> 26) & 1) != 0, size, (int)(w & 0x1F), (int)((w >> 5) & 0x1F),
            (int)(((w >> 10) & 0xFFF) << size));
        return true;
    }

    public static bool TryLoadFp(uint w, out LoadInfo load) {
        load = default;
        if ((w & 0x3B000000) != 0x39000000 || ((w >> 26) & 1) == 0) return false;
        uint size = (w >> 30) & 3;
        uint opc = (w >> 22) & 3;
        int scale;
        if (opc == 1) {
            scale = (int)size;
        } else if (opc == 3 && size == 0) {
            scale = 4;
        } else {
            return false;
        }

        load = new LoadInfo((int)(w & 0x1F), (int)((w >> 5) & 0x1F), (int)(((w >> 10) & 0xFFF) << scale),
            1 << scale);
        return true;
    }

    public static bool TryStur(uint w, out SturInfo stur) {
        stur = default;
        if ((w & 0x3B200C00) != 0x38000000) return false;
        uint size = (w >> 30) & 3;
        uint v = (w >> 26) & 1;
        uint opc = (w >> 22) & 3;
        int bytes;
        if (v == 1 && size == 0 && opc == 2) {
            bytes = 16;
        } else if (opc == 0) {
            bytes = 1 << (int)size;
        } else {
            return false;
        }

        stur = new SturInfo((int)(w & 0x1F), (int)((w >> 5) & 0x1F), (int)SignExtend((w >> 12) & 0x1FF, 9), bytes);
        return true;
    }

    public static bool TryMoviZero(uint w, out int rd) {
        rd = (int)(w & 0x1F);
        return (w & 0xFFFFFFE0) == 0x4F00E400;
    }

    public static bool TryAddImm(uint w, out int rd, out int rn, out ulong imm) {
        rd = (int)(w & 0x1F);
        rn = (int)((w >> 5) & 0x1F);
        imm = 0;
        if ((w & 0x7F800000) != 0x11000000) return false;
        int shift = (int)((w >> 22) & 1) * 12;
        imm = ((w >> 10) & 0xFFF) << shift;
        return true;
    }

    public static bool TryAddShifted(uint w, out AddShiftedInfo add) {
        add = default;
        if ((w & 0x7F200000) != 0x0B000000) return false;
        add = new AddShiftedInfo((int)(w & 0x1F), (int)((w >> 5) & 0x1F), (int)((w >> 16) & 0x1F),
            (int)((w >> 22) & 3), (int)((w >> 10) & 0x3F));
        return true;
    }

    public static bool TryMovReg(uint w, out int rd, out int rm) {
        rd = (int)(w & 0x1F);
        rm = (int)((w >> 16) & 0x1F);
        return (w & 0x7FE0FFE0) == 0x2A0003E0;
    }

    public static bool TryAdrp(ulong va, uint w, out ulong page, out int rd) {
        page = 0;
        rd = (int)(w & 0x1F);
        if ((w & 0x9F000000) != 0x90000000) return false;
        page = (ulong)((long)(va & ~0xFFFUL) + (PageImm(w) * 4096));
        return true;
    }

    public static bool TryAdr(Arm64Image im, ulong va, out ulong target, out int rd) {
        target = 0;
        rd = 0;
        if (!im.TryWord(va, out uint w)) return false;
        rd = (int)(w & 0x1F);
        if ((w & 0x9F000000) != 0x10000000) return false;
        target = (ulong)((long)va + PageImm(w));
        return true;
    }

    public static bool TryBranch(uint w, ulong va, out ulong target) {
        target = 0;
        if ((w & 0xFC000000) != 0x14000000) return false;
        target = (ulong)((long)va + (SignExtend(w & 0x3FFFFFF, 26) * 4));
        return true;
    }

    public static bool TryCondBranch(uint w, ulong va, out ulong target, out int cond) {
        target = 0;
        cond = (int)(w & 0xF);
        if ((w & 0xFF000010) != 0x54000000) return false;
        target = (ulong)((long)va + (SignExtend((w >> 5) & 0x7FFFF, 19) * 4));
        return true;
    }

    public static bool IsBl(uint w) => (w & 0xFC000000) == 0x94000000;

    public static bool TryFirstBranch(Arm64Image im, ulong start, int limit, out ulong target) {
        for (int i = 0; i < limit; i++) {
            ulong va = start + (ulong)(4 * i);
            if (!im.TryWord(va, out uint w)) break;
            if (TryBranch(w, va, out target)) return true;
        }

        target = 0;
        return false;
    }

    public static bool Cond(int cond, bool n, bool z, bool c, bool v) {
        bool result = (cond >> 1) switch {
            0 => z,
            1 => c,
            2 => n,
            3 => v,
            4 => c && !z,
            5 => n == v,
            6 => n == v && !z,
            _ => true
        };
        return (cond & 1) != 0 && cond != 0xF ? !result : result;
    }

    private static long PageImm(uint w) {
        long immlo = (w >> 29) & 3;
        long immhi = (w >> 5) & 0x7FFFF;
        long imm = (immhi << 2) | immlo;
        return (imm & (1L << 20)) != 0 ? imm - (1L << 21) : imm;
    }

    private static long SignExtend(uint value, int bits) {
        long v = value;
        long sign = 1L << (bits - 1);
        return (v ^ sign) - sign;
    }

    private static double Expand(uint imm8, bool f64) {
        uint sign = (imm8 >> 7) & 1;
        uint top = (imm8 >> 6) & 1;
        if (!f64) {
            uint bits = sign << 31;
            bits |= (top ^ 1) << 30;
            if (top != 0) bits |= 0x1Fu << 25;
            bits |= ((imm8 >> 4) & 3) << 23;
            bits |= (imm8 & 0xF) << 19;
            return BitConverter.Int32BitsToSingle(unchecked((int)bits));
        }

        ulong wide = (ulong)sign << 63;
        wide |= (ulong)(top ^ 1) << 62;
        if (top != 0) wide |= 0xFFUL << 54;
        wide |= (ulong)((imm8 >> 4) & 3) << 52;
        wide |= (ulong)(imm8 & 0xF) << 48;
        return BitConverter.Int64BitsToDouble(unchecked((long)wide));
    }

    private static bool TryBitMask(uint n, uint imms, uint immr, bool is64, out ulong value) {
        value = 0;
        if (!is64 && n != 0) return false;
        int len = HighestSetBit((n << 6) | (~imms & 0x3F));
        if (len < 1) return false;
        int size = 1 << len;
        uint levels = (uint)(size - 1);
        if ((imms & levels) == levels) return false;
        int s = (int)(imms & levels);
        int r = (int)(immr & levels);
        ulong sizeMask = size == 64 ? ulong.MaxValue : (1UL << size) - 1;
        ulong welem = s + 1 == 64 ? ulong.MaxValue : (1UL << (s + 1)) - 1;
        ulong elem = r == 0 ? welem : ((welem >> r) | (welem << (size - r))) & sizeMask;
        ulong result = 0;
        for (int i = 0; i < 64; i += size) result |= elem << i;
        value = is64 ? result : result & 0xFFFFFFFF;
        return true;
    }

    private static int HighestSetBit(uint value) {
        for (int i = 31; i >= 0; i--) {
            if ((value & (1u << i)) != 0) return i;
        }

        return -1;
    }
}

public static class Arm64Switch {
    private const int ScanInstructions = 0x200;
    private const int SimulationSteps = 4096;

    public static bool TryExtents(Arm64Image im, ulong fnStart, long fieldOffset, int firstAssetType, int count,
        out float[] table) {
        table = [];
        var stores = new List<ulong>();
        for (int i = 0; i < ScanInstructions; i++) {
            ulong va = fnStart + (ulong)(4 * i);
            if (!im.TryWord(va, out uint w)) break;
            if (IsFieldStore(w, fieldOffset)) stores.Add(va);
        }

        if (stores.Count < 2) return false;

        var values = new float[count];
        for (int t = 0; t < count; t++) {
            if (!TrySimulate(im, fnStart, stores[^1], fieldOffset, firstAssetType + t, out values[t])) return false;
        }

        table = values;
        return true;
    }

    private static bool IsFieldStore(uint w, long fieldOffset) =>
        Arm64Bits.TryStore(w, out var store) && store.Size == 2 && store.Rn != 31 && store.Offset == fieldOffset;

    private static bool TrySimulate(Arm64Image im, ulong fnStart, ulong lastStore, long fieldOffset, int assetType,
        out float value) {
        value = 0f;
        var regs = new ulong?[32];
        var fregs = new float?[32];
        bool have = false;
        Flags? flags = null;
        ulong pc = fnStart;

        for (int step = 0; step < SimulationSteps; step++) {
            if (pc < fnStart || pc > lastStore) break;
            if (!im.TryWord(pc, out uint w)) return false;

            if (Arm64Bits.TryStore(w, out var store) && store.Size == 2 && store.Rn != 31
                && store.Offset == fieldOffset) {
                if (store.Rt == 31) {
                    value = 0f;
                } else if (store.Fp) {
                    if (fregs[store.Rt] is not { } f) return false;
                    value = f;
                } else {
                    if (regs[store.Rt] is not { } r) return false;
                    value = Arm64Bits.F32(r);
                }

                have = true;
                pc += 4;
                continue;
            }

            if (Arm64Bits.IsBl(w)) {
                regs[0] = (uint)assetType;
                pc += 4;
                continue;
            }

            if (Arm64Bits.TryMovWide(w, out int movRd, out ulong movValue, out var kind, out _)) {
                regs[movRd] = kind == Arm64Bits.MovKind.Movk ? Arm64Bits.Merge(regs[movRd] ?? 0, w) : movValue;
                pc += 4;
                continue;
            }

            if (Arm64Bits.TryMovReg(w, out int movRegRd, out int movRegRm)) {
                regs[movRegRd] = regs[movRegRm];
                pc += 4;
                continue;
            }

            if (Arm64Bits.TryFmovImm(w, out double imm, out _, out int fmovRd)) {
                fregs[fmovRd] = (float)imm;
                pc += 4;
                continue;
            }

            if (Arm64Bits.TryFmovGprToFp(w, out int fpRd, out int fpRn, out _)) {
                fregs[fpRd] = fpRn == 31 ? 0f : regs[fpRn] is { } src ? Arm64Bits.F32(src) : null;
                pc += 4;
                continue;
            }

            if (Arm64Bits.TryCmpImm(w, out ulong cmpImm, out int cmpRn, out bool cmpIs64)) {
                flags = regs[cmpRn] is { } operand ? Compare(operand, cmpImm, cmpIs64) : null;
                pc += 4;
                continue;
            }

            if (Arm64Bits.TryBranch(w, pc, out ulong branch)) {
                pc = branch;
                continue;
            }

            if (Arm64Bits.TryCondBranch(w, pc, out ulong condTarget, out int cond)) {
                pc = flags is { } f && Arm64Bits.Cond(cond, f.N, f.Z, f.C, f.V) ? condTarget : pc + 4;
                continue;
            }

            pc += 4;
        }

        return have;
    }

    private readonly record struct Flags(bool N, bool Z, bool C, bool V);

    private static Flags Compare(ulong a, ulong b, bool is64) {
        if (is64) {
            ulong r = a - b;
            return new Flags((r >> 63) != 0, r == 0, a >= b, ((a ^ b) & (a ^ r) & (1UL << 63)) != 0);
        }

        uint a32 = (uint)a;
        uint b32 = (uint)b;
        uint r32 = a32 - b32;
        return new Flags((r32 >> 31) != 0, r32 == 0, a32 >= b32,
            ((a32 ^ b32) & (a32 ^ r32) & 0x80000000) != 0);
    }
}
