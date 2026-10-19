using System.Text.RegularExpressions;
using static EggIncognito.Core.Services.ProtoExtract.Arm64Operands;

namespace EggIncognito.Core.Services.ProtoExtract;

public static partial class BoostCatalogExtractor {
    public const string InitSymbol = "__GLOBAL__sub_I_boostmanager";
    public const string SignatureString = "TACHYON PRISM";

    private const int StructStride = 0x88;
    private const int DescFieldOffset = 0x30;
    private const long ElfNameOffset = 0x18;
    private const long ElfDimensionOffset = 0x48;
    private const long ElfDurationOffset = 0x70;
    private const long ElfMagnitudeOffset = 0x80;

    [GeneratedRegex("^[a-z][a-z0-9_]+$")]
    private static partial Regex IdPattern();

    public static Result Extract(byte[] bin) {
        var img = BinaryImage.Load(bin);
        return ExtractWith(bin, img?.Symbols ?? [], img?.Sections ?? []);
    }

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms,
        IReadOnlyList<MachoSections.Section> sections) {
        if (BinaryImage.Load(bin) is ElfImage) return ExtractElf(bin);

        var read = StaticInitCatalogReader.ReadWith(bin, syms, InitSymbol, IsBoostId);
        if (!read.Ok) return new Result(false, [], read.Diagnostics);

        var decoded = DecodeMemberDescriptions(bin, syms, sections, read.Entries);
        if (decoded is null) {
            var fallback = read.Entries.Select(e => new BoostEntry(e.Id, e.DisplayName, e.Description)).ToList();
            int fallbackDesc = fallback.Count(e => e.Description is not null);
            return new Result(true, fallback,
                $"{fallback.Count} boosts, {fallbackDesc} with description, member decode unavailable, reader descriptions only");
        }

        var outp = read.Entries
            .Select((e, k) => new BoostEntry(e.Id, e.DisplayName, decoded.GetValueOrDefault(k)))
            .ToList();
        return new Result(true, outp, $"{outp.Count} boosts, {decoded.Count} with description");
    }

    private static Result ExtractElf(byte[] bin) {
        var loc = InitArrayLocator.Create(bin);
        if (loc is null || !loc.TryLocateByString(SignatureString, out ulong s, out ulong e))
            return new Result(false, [], $"boostmanager init not located via '{SignatureString}' on ELF");

        var scan = StructInitReader.ReadRange(bin, s, e, writeback: true, trackHeap: true);
        if (!scan.Ok) return new Result(false, [], scan.Diagnostics);

        var flat = StructInitReader.Flatten(scan.Structs);
        var outp = new List<BoostEntry>();
        foreach (long at in flat.Bytes.Keys.Order()) {
            ulong rec = unchecked((ulong)at);
            if (StructInitReader.IsSynthetic(rec)) continue;
            if (!StructInitReader.TryLibcxxAt(flat, rec, out string id) || !IsBoostId(id)) continue;
            if (!flat.TryInt(at + ElfDimensionOffset, 4, out _)
                || !flat.TryFloat64(at + ElfDurationOffset, out double duration) || !double.IsFinite(duration)
                || !flat.TryFloat64(at + ElfMagnitudeOffset, out double magnitude) || !double.IsFinite(magnitude)) {
                continue;
            }

            if (outp.Any(x => x.Id == id)) continue;
            string? name = StructInitReader.TryLibcxxAt(flat, rec + ElfNameOffset, out string n) && n.Length > 0
                ? n
                : null;
            string? desc = StructInitReader.TryLibcxxAt(flat, rec + DescFieldOffset, out string d)
                ? ElfDescription(d)
                : null;
            outp.Add(new BoostEntry(id, name, desc));
        }

        if (outp.Count == 0) return new Result(false, [], "no boost records decoded from boostmanager init stores");
        int described = outp.Count(x => x.Description is not null);
        return new Result(true, outp, $"{outp.Count} boosts, {described} with description (ELF record decode)");
    }

    private static string? ElfDescription(string raw) {
        string body = raw.Length > 0 && raw[0] == '\x1b' ? raw[1..] : raw;
        if (raw.Length > 0 && raw[0] == '\x1b' && body.StartsWith('z')) body = body[1..];
        return body.Length == 0 ? null : body;
    }

    private static bool IsBoostId(string s)
        => s.Length >= 4 && IdPattern().IsMatch(s) && !s.StartsWith("bd", StringComparison.Ordinal);

    private static int RegIndex(string reg) {
        string digits = reg.Length > 1 && reg[0] is 'x' or 'w' ? reg[1..] : reg;
        return int.TryParse(digits, out int n) ? n : -1;
    }

    private static Dictionary<int, string>? DecodeMemberDescriptions(byte[] bin,
        IReadOnlyList<MachoSymbols.Symbol> syms,
        IReadOnlyList<MachoSections.Section> sections, IReadOnlyList<StaticInitCatalogReader.Entry> entries) {
        var expected = new Dictionary<int, string>();
        for (int k = 0; k < entries.Count; k++) {
            if (entries[k].Description is { } d)
                expected[k] = d;
        }

        if (expected.Count == 0) return null;

        var lst = Arm64DataTableReader.ListWith(bin, syms, [InitSymbol], 100_000);
        if (!lst.Ok) return null;

        var image = new Arm64Image(bin, BinaryImage.Load(bin)!);
        var tracker = new Arm64PageTracker { Image = image };
        var allocRegs = new HashSet<int>();
        ulong? x0Imm = null;
        ulong? activeAlloc = null;
        var storeTargets = new List<ulong>();
        var descStores = new List<(ulong Va, ulong AllocSize)>();
        var sizeText = new Dictionary<ulong, string>();
        bool conflict = false;

        bool RecordStoreTarget(string memToken, out ulong va) {
            va = 0;
            if (!TryMem(memToken, out string baseReg, out ulong off) ||
                !tracker.TryValue(RegIndex(baseReg), out ulong baseVa)) {
                return false;
            }

            va = baseVa + off;
            storeTargets.Add(va);
            return true;
        }

        foreach (var i in lst.Instructions) {
            if (image.TryWord(i.Va, out uint word)) tracker.Track(i.Va, word);
            var ops = SplitOps(i.Operands);
            bool targetsX0 = ops.Count >= 1 && ops[0] is "x0" or "w0";

            switch (i.Mnemonic) {
                case "add":
                    if (targetsX0) x0Imm = null;
                    if (ops.Count == 3 && tracker.TryValue(RegIndex(ops[0]), out ulong full) &&
                        activeAlloc is { } sz && TryReadDescription(bin, sections, full, out string text)) {
                        if (sizeText.TryGetValue(sz, out string? prev) && prev != text) conflict = true;
                        else sizeText[sz] = text;
                    }

                    break;

                case "mov":
                    if (targetsX0) x0Imm = null;
                    if (ops.Count == 2) {
                        if (TryImm(ops[1], out ulong mc)) {
                            if (targetsX0) x0Imm = mc;
                        } else if (LooksLikeGpr(ops[1]) && allocRegs.Contains(RegIndex(ops[1]))) {
                            allocRegs.Add(RegIndex(ops[0]));
                        }
                    }

                    break;

                case "movz":
                    if (targetsX0) {
                        x0Imm = ops.Count >= 2 && TryImm(ops[1], out ulong mz) ? mz << ShiftOf(ops) : null;
                    }

                    break;

                case "orr":
                    if (targetsX0) {
                        x0Imm = ops.Count == 3 && ops[1] is "wzr" or "xzr" && TryImm(ops[2], out ulong oi)
                            ? oi
                            : null;
                    }

                    break;

                case "bl":
                case "blr":
                    if (x0Imm is { } allocSize) {
                        activeAlloc = allocSize;
                        allocRegs.Clear();
                        allocRegs.Add(0);
                    } else {
                        allocRegs.Clear();
                    }

                    x0Imm = null;
                    for (int r = 0; r <= 17; r++) tracker.Invalidate(r);
                    break;

                case "str":
                case "stur":
                    if (ops.Count >= 2 && RecordStoreTarget(ops[^1], out ulong strVa)
                                       && activeAlloc is { } strAlloc && allocRegs.Contains(RegIndex(ops[0]))) {
                        descStores.Add((strVa, strAlloc));
                    }

                    break;

                case "strb":
                case "sturb":
                case "strh":
                case "sturh":
                    if (ops.Count >= 2) RecordStoreTarget(ops[^1], out _);
                    break;

                case "stp":
                    if (ops.Count >= 3) RecordStoreTarget(ops[^1], out _);
                    break;

                default:
                    if (targetsX0 && !IsNonWriting(i.Mnemonic)) x0Imm = null;
                    break;
            }
        }

        if (conflict || sizeText.Count == 0 || descStores.Count == 0) return null;

        var candidates = new HashSet<ulong>(storeTargets);
        foreach ((ulong va, ulong _) in descStores) {
            for (int k = 0; k < entries.Count; k++) {
                ulong rel = (ulong)(DescFieldOffset + k * StructStride);
                if (va >= rel) candidates.Add(va - rel);
            }
        }

        foreach (ulong candidateBase in candidates.Order()) {
            if (TryGroupsForBase(candidateBase, descStores, sizeText, entries.Count, expected, out var groups))
                return groups;
        }

        return null;
    }

    private static bool TryGroupsForBase(ulong baseVa, IReadOnlyList<(ulong Va, ulong AllocSize)> descStores,
        Dictionary<ulong, string> sizeText, int entryCount, IReadOnlyDictionary<int, string> expected,
        out Dictionary<int, string> groups) {
        groups = [];
        foreach ((ulong va, ulong size) in descStores) {
            if (va < baseVa) continue;
            ulong rel = va - baseVa;
            if (rel % StructStride != DescFieldOffset) continue;
            int k = (int)(rel / StructStride);
            if (k >= entryCount) continue;
            if (!sizeText.TryGetValue(size, out string? text)) continue;
            if (groups.TryGetValue(k, out string? prev) && prev != text) return false;
            groups[k] = text;
        }

        if (groups.Count == 0) return false;
        foreach (var kv in expected) {
            if (!groups.TryGetValue(kv.Key, out string? g) || g != kv.Value)
                return false;
        }

        return true;
    }

    private static readonly string[] DescriptionSections = ["__cstring"];

    private static bool TryReadDescription(byte[] bin, IReadOnlyList<MachoSections.Section> sections, ulong va,
        out string desc) {
        desc = "";
        string s = BinaryStrings.ReadCstr(bin, sections, va, DescriptionSections);
        if (s.Length < 2 || s[0] != '\x1b') return false;
        string body = s[1] == 'z' ? s[2..] : s[1..];
        if (body.Length == 0) return false;
        desc = body;
        return true;
    }

    private static bool IsNonWriting(string mnemonic)
        => mnemonic is "cmp" or "cmn" or "ccmp" or "tst" or "fcmp" or "b" or "ret" or "nop"
           || mnemonic.StartsWith("b.", StringComparison.Ordinal)
           || mnemonic.StartsWith("cb", StringComparison.Ordinal)
           || mnemonic.StartsWith("tb", StringComparison.Ordinal);

    public readonly record struct BoostEntry(string Id, string? DisplayName, string? Description);

    public readonly record struct Result(bool Ok, IReadOnlyList<BoostEntry> Entries, string Diagnostics);
}
