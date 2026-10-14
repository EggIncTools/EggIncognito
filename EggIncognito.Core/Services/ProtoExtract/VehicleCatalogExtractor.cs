namespace EggIncognito.Core.Services.ProtoExtract;

public static class VehicleCatalogExtractor {
    public const string InitSymbol = "__GLOBAL__sub_I_vehicledata.cpp";
    public const string SignatureString = "HYPERLOOP TRAIN";
    private const long Stride = 0xF0;
    private const long ElfStride = 0x140;
    private const long CapacityOff = 0x18;
    private const long LengthOff = 0xE0;
    private const long ElfLengthOff = 0x130;

    public static Result Read(byte[] bin, string initSymbol = InitSymbol) {
        var img = BinaryImage.Load(bin);
        return ReadWith(bin, img?.Symbols ?? [], initSymbol);
    }

    public static Result ReadWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, string initSymbol = InitSymbol) {
        var img = BinaryImage.Load(bin);
        if (img is ElfImage) return ReadElf(bin);
        if (img is null) return new Result(false, [], "binary is neither Mach-O nor ELF");
        var scan = StructInitReader.ReadWith(bin, syms, initSymbol);
        return !scan.Ok ? new Result(false, [], scan.Diagnostics) : Build(bin, img, scan, Stride, LengthOff);
    }

    private static Result ReadElf(byte[] bin) {
        var loc = InitArrayLocator.Create(bin);
        if (loc is null) return new Result(false, [], "no binary image");
        var img = BinaryImage.Load(bin);
        if (img is null) return new Result(false, [], "no binary image");
        var best = new Result(false, [], "vehicledata init not located via signature on ELF");
        foreach ((ulong s0, ulong e0) in loc.LocateAllByString(SignatureString)) {
            var scan = StructInitReader.ReadRange(bin, s0, e0);
            if (!scan.Ok) continue;
            var built = Build(bin, img, scan, ElfStride, ElfLengthOff);
            if (built.Entries.Count > best.Entries.Count) best = built;
        }

        return best;
    }

    private static Result Build(byte[] bin, IBinaryImage img, StructInitReader.Result scan, long stride,
        long lengthOff) {
        if (scan.Structs.Count == 0) return new Result(false, [], "no struct bases");

        var s = StructInitReader.MergeByAbsoluteVa(scan.Structs);
        var entries = new List<VehicleEntry>();
        for (int i = 0; ; i++) {
            long slot = i * stride;
            if (!s.TryInt(slot + CapacityOff, 8, out long capacity) || !IsPlausibleCapacity(capacity)) break;
            entries.Add(new VehicleEntry(i, ResolveName(bin, img, s, slot), capacity,
                ReadLength(s, slot + lengthOff)));
        }

        return new Result(true, entries, $"{entries.Count} vehicles, {entries.Count(e => e.Name is not null)} named");
    }

    private static string? ResolveName(byte[] bin, IBinaryImage img, StructInitReader.StructInit s, long nameOff)
        => ResolveAt(bin, img, s, nameOff) ?? ResolveAt(bin, img, s, nameOff + 1);

    private static string? ResolveAt(byte[] bin, IBinaryImage img, StructInitReader.StructInit s, long off)
        => StructInitReader.TryLibcxxString(bin, img, s, off, out string name) ? IsName(name) : null;

    private static double ReadLength(StructInitReader.StructInit s, long offset)
        => s.TryFloat64(offset, out double v) && double.IsFinite(v) && v is > 0 and < 1000 ? v : 0;

    private static bool IsPlausibleCapacity(long c) => c is > 0 and <= 1_000_000_000_000;

    private static string? IsName(string s) => BinaryStrings.IsName(s, " .'", allowDigitStart: true);

    public readonly record struct VehicleEntry(int Index, string? Name, long Capacity, double Length = 0);

    public readonly record struct Result(bool Ok, IReadOnlyList<VehicleEntry> Entries, string Diagnostics);
}
