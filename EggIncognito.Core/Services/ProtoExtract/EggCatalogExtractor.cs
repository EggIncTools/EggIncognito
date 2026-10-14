using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class EggCatalogExtractor {
    public const string SignatureString = "A regular egg. Edible and incredible.";
    private const long ValueFromNameDelta = 0x30;
    private const long ElfHatcheryFromRecord = 0xf0;
    private const long MachoRecordStride = 0x110;
    private const long MachoHatcheryFromRecord = 0xb8;
    private const long MachoFirstHatcheryFromBase = 0x40;
    private const long MachoSecondNameFromBase = 0x98;
    private const long MachoChainedNameFromBase = -0x40;

    public static Result Read(byte[] bin, string initSymbol = "__GLOBAL__sub_I_eggdata.cpp") {
        var img = BinaryImage.Load(bin);
        return ReadWith(bin, img?.Symbols ?? [], initSymbol);
    }

    public static Result ExtractAuto(byte[] bin) {
        var img = BinaryImage.Load(bin);
        if (img is ElfImage) {
            var loc = InitArrayLocator.Create(bin);
            if (loc is null || !loc.TryLocateByString(SignatureString, out ulong s, out ulong e))
                return new Result(false, [], "eggdata init not located via signature on ELF");
            return ExtractRange(bin, s, e);
        }

        return ReadWith(bin, img?.Symbols ?? []);
    }

    public static Result ExtractRange(byte[] bin, ulong startVa, ulong endVa) {
        var scan = StructInitReader.ReadRange(bin, startVa, endVa, writeback: true);
        if (!scan.Ok) return new Result(false, [], scan.Diagnostics);

        var img = BinaryImage.Load(bin);
        var flat = new Dictionary<ulong, byte>();
        var flatSrc = new Dictionary<ulong, ulong>();
        foreach (var s in scan.Structs) {
            foreach (var (off, b) in s.Bytes) flat[s.BaseVa + (ulong)off] = b;
            foreach (var (off, src) in s.Pointers) flatSrc[s.BaseVa + (ulong)off] = src;
        }

        var found = new List<(ulong Va, string Section, string Name, double Value)>();
        foreach (ulong va in flat.Keys) {
            if (!flat.TryGetValue(va, out byte sizeByte) || sizeByte == 0 || (sizeByte & 1) != 0) continue;
            int len = sizeByte >> 1;
            if (len is < 2 or > 22) continue;
            if (!TryFlatFloat64(flat, va + (ulong)ValueFromNameDelta, out double v) || !IsPlausible(v)) continue;
            string? name = ReadInlineName(flat, va + 1, len) ?? ReadSourceName(bin, img, flatSrc, va + 1, len);
            if (name is null) continue;
            string section = img is not null && img.TryVaToFileOffset(va, out _, out var owner) ? owner.Name : "";
            found.Add((va, section, name, v));
        }

        string primary = found.GroupBy(f => f.Section).OrderByDescending(g => g.Count())
            .Select(g => g.Key).FirstOrDefault() ?? "";
        var eggs = found.Where(f => f.Section == primary).OrderBy(f => f.Va).ToList();
        var entries = new List<EggEntry>(eggs.Count);
        for (int i = 0; i < eggs.Count; i++) {
            entries.Add(new EggEntry(i, eggs[i].Name, eggs[i].Value,
                ReadFlatFloat32(flat, eggs[i].Va + (ulong)ElfHatcheryFromRecord)));
        }

        return new Result(true, entries,
            $"{entries.Count} eggs (elf {primary}), {entries.Count(e => e.Name is not null)} named");
    }

    private static string? ReadInlineName(Dictionary<ulong, byte> flat, ulong start, int len) {
        var sb = new StringBuilder(len);
        for (int k = 0; k < len; k++) {
            if (!flat.TryGetValue(start + (ulong)k, out byte b)) return null;
            sb.Append((char)b);
        }

        return IsName(sb.ToString());
    }

    private static string? ReadSourceName(byte[] bin, IBinaryImage? img, Dictionary<ulong, ulong> flatSrc,
        ulong start, int len) {
        if (img is null || !flatSrc.TryGetValue(start, out ulong src)) return null;
        if (!img.TryVaToFileOffset(src, out int fo, out _) || fo < 0 || fo + len > bin.Length) return null;
        return IsName(Encoding.ASCII.GetString(bin, fo, len));
    }

    private static bool TryFlatFloat64(Dictionary<ulong, byte> flat, ulong va, out double value) {
        value = 0;
        ulong raw = 0;
        for (int k = 0; k < 8; k++) {
            if (!flat.TryGetValue(va + (ulong)k, out byte b)) return false;
            raw |= (ulong)b << (k * 8);
        }

        value = Arm64Bits.F64(raw);
        return double.IsFinite(value);
    }

    private static double ReadFlatFloat32(Dictionary<ulong, byte> flat, ulong va) {
        uint raw = 0;
        for (int k = 0; k < 4; k++) {
            if (!flat.TryGetValue(va + (ulong)k, out byte b)) return 0;
            raw |= (uint)b << (k * 8);
        }

        float v = Arm64Bits.F32(raw);
        return float.IsFinite(v) && v >= 0 ? v : 0;
    }

    public static Result ReadWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms,
        string initSymbol = "__GLOBAL__sub_I_eggdata.cpp") {
        var scan = StructInitReader.ReadWith(bin, syms, initSymbol);
        if (!scan.Ok) return new Result(false, [], scan.Diagnostics);
        var img = BinaryImage.Load(bin);
        if (img is null) return new Result(false, [], "binary is neither Mach-O nor ELF");

        var flat = new Dictionary<ulong, byte>();
        foreach (var st in scan.Structs) {
            foreach ((long off, byte b) in st.Bytes) flat[st.BaseVa + (ulong)off] = b;
        }

        var entries = new List<EggEntry>();
        if (scan.Structs.Count == 0) return new Result(true, entries, "0 eggs, 0 named");

        ulong recordBase = scan.Structs[0].BaseVa;
        for (int i = 0; i <= scan.Structs.Count; i++) {
            var s = scan.Structs[i <= 1 ? 0 : i - 1];
            long nameOff = i switch {
                0 => 0L,
                1 => MachoSecondNameFromBase,
                _ => MachoChainedNameFromBase
            };
            if (!s.TryFloat64(nameOff + ValueFromNameDelta, out double value) || !IsPlausible(value)) break;

            ulong extentVa = i == 0
                ? recordBase + (ulong)MachoFirstHatcheryFromBase
                : recordBase + (ulong)((i * MachoRecordStride) + MachoHatcheryFromRecord);
            entries.Add(new EggEntry(i, ResolveName(bin, img, s, nameOff), value,
                ReadFlatFloat32(flat, extentVa)));
        }

        return new Result(true, entries, $"{entries.Count} eggs, {entries.Count(e => e.Name is not null)} named");
    }

    private static string? ResolveName(byte[] bin, IBinaryImage img, StructInitReader.StructInit s, long nameOff)
        => StructInitReader.TryLibcxxString(bin, img, s, nameOff, out string name) ? IsName(name) : null;

    private static string? IsName(string s) => BinaryStrings.IsName(s, " .");

    private static bool IsPlausible(double d) => double.IsFinite(d) && Math.Abs(d) is >= 1e-9 and <= 1e18;

    public readonly record struct EggEntry(int Index, string? Name, double BaseValue, double HatcheryExtent = 0);

    public readonly record struct Result(bool Ok, IReadOnlyList<EggEntry> Entries, string Diagnostics);
}
