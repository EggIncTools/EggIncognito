namespace EggIncognito.Core.Services.ProtoExtract;

public enum TableElemType {
    F32,
    F64,
    I32,
    I64,
    U32,
    U64
}

public static class Arm64ConstSectionReader {
    public static int ElemSize(TableElemType t) => t switch {
        TableElemType.F32 or TableElemType.I32 or TableElemType.U32 => 4,
        _ => 8
    };

    public static DumpResult Dump(byte[] bin, ulong va, int count, TableElemType elem) {
        if (bin is null || bin.Length < 64) return new DumpResult(false, va, "", "", [], "binary too short");
        if (count is <= 0 or > 4096) return new DumpResult(false, va, "", "", [], "count out of range (1..4096)");

        var img = BinaryImage.Load(bin);
        if (img is null || !img.TryVaToFileOffset(va, out int fileOff, out var owner))
            return new DumpResult(false, va, "", "", [], $"va 0x{va:x} not in any mapped section");

        int size = ElemSize(elem);
        long need = fileOff + (long)count * size;
        if (need > bin.Length)
            return new DumpResult(false, va, owner.Segment, owner.Name, [], "table extends past end of file");

        var values = new List<double>(count);
        for (int i = 0; i < count; i++) {
            int o = fileOff + i * size;
            double v = elem switch {
                TableElemType.F32 => BitConverter.ToSingle(bin.AsSpan(o)),
                TableElemType.F64 => BitConverter.ToDouble(bin.AsSpan(o)),
                TableElemType.I32 => BitConverter.ToInt32(bin.AsSpan(o)),
                TableElemType.I64 => BitConverter.ToInt64(bin.AsSpan(o)),
                TableElemType.U32 => BitConverter.ToUInt32(bin.AsSpan(o)),
                TableElemType.U64 => BitConverter.ToUInt64(bin.AsSpan(o)),
                _ => 0
            };
            values.Add(v);
        }

        return new DumpResult(true, va, owner.Segment, owner.Name, values, "ok");
    }

    public static bool TryParseElem(string? s, out TableElemType elem) {
        elem = TableElemType.F64;
        if (string.IsNullOrWhiteSpace(s)) return false;
        switch (s.Trim().ToLowerInvariant()) {
            case "f32" or "float" or "single":
                elem = TableElemType.F32;
                return true;
            case "f64" or "double":
                elem = TableElemType.F64;
                return true;
            case "i32" or "int":
                elem = TableElemType.I32;
                return true;
            case "i64" or "long":
                elem = TableElemType.I64;
                return true;
            case "u32" or "uint":
                elem = TableElemType.U32;
                return true;
            case "u64" or "ulong":
                elem = TableElemType.U64;
                return true;
            default: return false;
        }
    }

    public readonly record struct DumpResult(
        bool Ok,
        ulong Va,
        string Segment,
        string Section,
        IReadOnlyList<double> Values,
        string Diagnostics);
}
