namespace EggIncognito.Core.Services.ProtoExtract;

public static class BoostEffectExtractor {
    public const string InitSymbol = BoostCatalogExtractor.InitSymbol;
    private const string ElfSignature = "jimbos_blue_v2";
    private const long Stride = 0x88;
    private const long DimensionOff = 0x48;
    private const long DurationOff = 0x70;
    private const long MagnitudeOff = 0x80;
    private const int ShortStringMax = 22;

    private static readonly (string Id, int Dimension, double Duration, double Magnitude)[] Anchors = [
        ("jimbos_blue_v2", 2, 1200, 3),
        ("jimbos_purple_v2", 2, 900, 10),
        ("tachyon_prism_purple_big", 4, 7200, 100),
        ("tachyon_prism_orange", 4, 600, 1000)
    ];

    public static Result Extract(byte[] bin) {
        var img = BinaryImage.Load(bin);
        return ExtractWith(bin, img?.Symbols ?? [], img?.Sections ?? []);
    }

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms,
        IReadOnlyList<MachoSections.Section> sections) {
        var catalog = BoostCatalogExtractor.ExtractWith(bin, syms, sections);
        if (!catalog.Ok || catalog.Entries.Count == 0) return new Result(false, [], catalog.Diagnostics);

        StructInitReader.Result scan;
        if (BinaryImage.Load(bin) is ElfImage) {
            var loc = InitArrayLocator.Create(bin);
            if (loc is null || !loc.TryLocateByString(ElfSignature, out ulong s, out ulong e))
                return new Result(false, [], $"boostmanager init not located via '{ElfSignature}' on ELF");
            scan = StructInitReader.ReadRange(bin, s, e);
        } else {
            scan = StructInitReader.ReadWith(bin, syms, InitSymbol);
        }

        if (!scan.Ok) return new Result(false, [], scan.Diagnostics);
        if (scan.Structs.Count == 0) return new Result(false, [], "no struct bases");

        var merged = StructInitReader.MergeByAbsoluteVa(scan.Structs);
        if (!TryFindFirstRecord(merged, catalog.Entries[0].Id, out long origin))
            return new Result(false, [],
                $"record 0 ('{catalog.Entries[0].Id}') not located by inline id or anchor signature");

        var catalogIds = catalog.Entries.Select(c => c.Id).ToList();
        var ids = ResolveRecordIds(merged, origin, catalogIds, out string? idDiag);
        if (ids is null) return new Result(false, [], idDiag ?? "record ids unresolved");

        var names = catalog.Entries.ToDictionary(c => c.Id, c => c.DisplayName, StringComparer.Ordinal);
        var entries = new List<BoostEffectEntry>();
        var skipped = new List<string>();
        for (int k = 0; k < ids.Count; k++) {
            long row = origin + k * Stride;
            string id = ids[k];
            if (!merged.TryInt(row + DimensionOff, 4, out long dim) ||
                !merged.TryFloat64(row + DurationOff, out double duration) ||
                !merged.TryFloat64(row + MagnitudeOff, out double magnitude) ||
                !double.IsFinite(duration) || !double.IsFinite(magnitude)) {
                skipped.Add(id);
                continue;
            }

            entries.Add(new BoostEffectEntry(id, names.GetValueOrDefault(id), (int)dim, duration, magnitude));
        }

        var misses = Anchors
            .Where(a => entries.FirstOrDefault(e => e.Id == a.Id) is not { Id: not null } e
                        || e.Dimension != a.Dimension || e.DurationSeconds != a.Duration || e.Magnitude != a.Magnitude)
            .Select(a => a.Id)
            .ToList();
        if (misses.Count > 0)
            return new Result(false, entries, "anchor mismatch: " + string.Join(", ", misses));

        string tail = skipped.Count == 0 ? "" : $", {skipped.Count} undecoded: {string.Join(", ", skipped)}";
        return new Result(true, entries, $"{entries.Count} of {catalog.Entries.Count} boosts decoded{tail}");
    }

    private static List<string>? ResolveRecordIds(StructInitReader.StructInit s, long origin,
        List<string> catalogIds, out string? diagnostics) {
        diagnostics = null;
        var known = new HashSet<string>(catalogIds, StringComparer.Ordinal);
        var inline = new string?[catalogIds.Count];
        for (int k = 0; k < catalogIds.Count; k++) {
            if (TryRecordId(s, origin + k * Stride, out string id) && known.Contains(id)) inline[k] = id;
        }

        var matched = new HashSet<string>(inline.OfType<string>(), StringComparer.Ordinal);
        if (matched.Count != inline.Count(x => x is not null)) {
            diagnostics = "duplicate inline record id";
            return null;
        }

        var heapIds = new Queue<string>(catalogIds.Where(c => !matched.Contains(c) && c.Length > ShortStringMax));
        var ids = new List<string>(catalogIds.Count);
        for (int k = 0; k < inline.Length; k++) {
            if (inline[k] is { } id) {
                ids.Add(id);
            } else if (heapIds.Count > 0) {
                ids.Add(heapIds.Dequeue());
            } else {
                diagnostics = $"record {k} has no inline id and no heap-length catalog id remains";
                return null;
            }
        }

        var unplaced = catalogIds.Where(c => !ids.Contains(c)).ToList();
        if (unplaced.Count > 0) {
            diagnostics = "catalog ids without a record: " + string.Join(", ", unplaced);
            return null;
        }

        return ids;
    }

    private static bool TryRecordId(StructInitReader.StructInit s, long row, out string id) {
        if (s.TryInlineStringComplete(row, out id) && LooksLikeId(id)) return true;
        if (s.TryInt(row, 1, out long lenByte) && (lenByte & 1) == 0 && lenByte > 0
            && s.TryInlineStringComplete(row + 1, out id) && id.Length == lenByte >> 1 && LooksLikeId(id)) {
            return true;
        }

        id = "";
        return false;
    }

    private static bool LooksLikeId(string s) =>
        s.Length >= 4 && char.IsAsciiLetterLower(s[0])
        && s.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');

    private static bool TryFindFirstRecord(StructInitReader.StructInit s, string firstId, out long origin) {
        var offsets = s.Bytes.Keys.Order().ToList();
        foreach (long off in offsets) {
            if (!TryRecordId(s, off, out string inline) || inline != firstId) continue;
            if (!s.TryInt(off + DimensionOff, 4, out _) || !s.TryFloat64(off + MagnitudeOff, out _)) continue;
            origin = off;
            return true;
        }

        var anchor = Anchors.FirstOrDefault(a => a.Id == firstId);
        if (anchor.Id is not null) {
            foreach (long off in offsets) {
                if (s.TryInt(off + DimensionOff, 4, out long dim) && dim == anchor.Dimension
                    && s.TryFloat64(off + DurationOff, out double dur) && dur == anchor.Duration
                    && s.TryFloat64(off + MagnitudeOff, out double mag) && mag == anchor.Magnitude) {
                    origin = off;
                    return true;
                }
            }
        }

        origin = 0;
        return false;
    }

    public readonly record struct BoostEffectEntry(string Id, string? DisplayName, int Dimension,
        double DurationSeconds, double Magnitude);

    public readonly record struct Result(bool Ok, IReadOnlyList<BoostEffectEntry> Entries, string Diagnostics);
}
