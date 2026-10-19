using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services.ProtoExtract;

public static partial class EventIconExtractor {
    public const string SignatureString = "earnings-boost";
    private const long RecordStride = 0x80;
    private const long IconFunctionOffset = 0x50;
    private const long InvokeSlotOffset = 0x30;
    private const int MaxEvents = 64;

    [GeneratedRegex(@"^_ZN1A3IMG\d+([a-z0-9_]+)E$")]
    private static partial Regex IconGlobalRegex();

    public readonly record struct IconEntry(int Index, string EventType, string IconStem);

    public readonly record struct Result(bool Ok, IReadOnlyList<IconEntry> Entries, string Diagnostics);

    public static Result ExtractAuto(byte[] bin) {
        var img = BinaryImage.Load(bin);
        if (img is not ElfImage elf)
            return new Result(false, [], "event icons require the Android ELF; iOS Mach-O is desymbolized");

        var loc = InitArrayLocator.Create(bin);
        if (loc is null || !loc.TryLocateByString(SignatureString, out ulong start, out ulong end))
            return new Result(false, [], "live events init not located by signature on ELF");

        var scan = StructInitReader.ReadRange(bin, start, end, writeback: true);
        if (!scan.Ok) return new Result(false, [], scan.Diagnostics);

        var bytes = new Dictionary<long, byte>();
        var pointers = new Dictionary<long, ulong>();
        foreach (var s in scan.Structs) {
            foreach (var kv in s.Bytes) bytes[(long)s.BaseVa + kv.Key] = kv.Value;
            foreach (var kv in s.Pointers) pointers[(long)s.BaseVa + kv.Key] = kv.Value;
        }

        ulong table = 0;
        long count = 0;
        foreach (var s in scan.Structs) {
            if (s.TryPointer(0, out ulong p) && s.TryInt(8, 4, out long n) && n is > 0 and <= MaxEvents
                && bytes.ContainsKey((long)p)) {
                table = p;
                count = n;
                break;
            }
        }

        if (table == 0) return new Result(false, [], "live events table pointer and count not found in init");

        var stems = new Dictionary<ulong, string>();
        foreach (var sym in elf.Symbols) {
            if (sym.Kind != SymbolKind.Object) continue;
            var m = IconGlobalRegex().Match(sym.Name);
            if (m.Success) stems[sym.Value] = m.Groups[1].Value;
        }

        var flat = new StructInitReader.StructInit(0, bytes, pointers, new Dictionary<long, ulong>());
        var entries = new List<IconEntry>();
        for (int k = 0; k < count; k++) {
            long rec = (long)table + k * RecordStride;
            if (!StructInitReader.TryLibcxxString(bin, img, flat, rec, out string id) || id.Length == 0) continue;
            if (!pointers.TryGetValue(rec + IconFunctionOffset, out ulong vtable)) continue;
            if (!elf.TryResolveSlot(vtable + InvokeSlotOffset, out ulong invoke, out _)) continue;
            string? stem = StemAt(bin, img, elf, invoke, stems);
            if (stem is not null) entries.Add(new IconEntry(entries.Count, id, stem));
        }

        return entries.Count == 0
            ? new Result(false, [], $"no event icon lambdas resolved across {count} records")
            : new Result(true, entries, $"{entries.Count} event icons from {count} records");
    }

    private const uint LdrUnsignedMask = 0xFFC00000;
    private const uint LdrX64 = 0xF9400000;
    private const uint LdrRnMask = 0x3E0;
    private const uint Ret = 0xD65F03C0;

    private static string? StemAt(byte[] bin, IBinaryImage img, ElfImage elf, ulong invoke,
        Dictionary<ulong, string> stems) {
        if (Word(bin, img, invoke) is not { } w0 || Word(bin, img, invoke + 4) is not { } w1
            || Word(bin, img, invoke + 8) is not { } w2 || Word(bin, img, invoke + 12) is not { } w3)
            return null;
        if (!Arm64Bits.TryAdrp(invoke, w0, out ulong page, out _)) return null;
        if ((w1 & LdrUnsignedMask) != LdrX64 || (w2 & ~LdrRnMask) != LdrX64 || w3 != Ret) return null;
        ulong slot = page + ((w1 >> 10) & 0xFFF) * 8;
        if (!elf.TryResolveSlot(slot, out ulong target, out string? symbol)) return null;
        if (symbol is not null && IconGlobalRegex().Match(symbol) is { Success: true } m) return m.Groups[1].Value;
        return stems.GetValueOrDefault(target);
    }

    private static uint? Word(byte[] bin, IBinaryImage img, ulong va) {
        if (!img.TryVaToFileOffset(va, out int off, out _) || off < 0 || off + 4 > bin.Length) return null;
        return BinaryPrimitives.ReadUInt32LittleEndian(bin.AsSpan(off, 4));
    }
}
