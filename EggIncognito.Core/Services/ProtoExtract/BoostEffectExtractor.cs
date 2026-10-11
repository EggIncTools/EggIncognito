namespace EggIncognito.Core.Services.ProtoExtract;

public static class BoostEffectExtractor {
    public const string InitSymbol = BoostCatalogExtractor.InitSymbol;
    private const long Stride = 0x88;
    private const long DimensionOff = 0x48;
    private const long DurationOff = 0x70;
    private const long MagnitudeOff = 0x80;

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
        if (BinaryImage.Load(bin) is ElfImage)
            return new Result(false, [], "boost effect record decode is Mach-O only");

        var catalog = BoostCatalogExtractor.ExtractWith(bin, syms, sections);
        if (!catalog.Ok || catalog.Entries.Count == 0) return new Result(false, [], catalog.Diagnostics);

        var scan = StructInitReader.ReadWith(bin, syms, InitSymbol);
        if (!scan.Ok) return new Result(false, [], scan.Diagnostics);
        if (scan.Structs.Count == 0) return new Result(false, [], "no struct bases");

        var merged = StructInitReader.MergeByAbsoluteVa(scan.Structs);
        if (!TryFindFirstRecord(merged, catalog.Entries[0].Id, out long origin))
            return new Result(false, [],
                $"record 0 ('{catalog.Entries[0].Id}') not located by inline id or anchor signature");

        var entries = new List<BoostEffectEntry>();
        var skipped = new List<string>();
        for (int k = 0; k < catalog.Entries.Count; k++) {
            long row = origin + k * Stride;
            var id = catalog.Entries[k];
            if (!merged.TryInt(row + DimensionOff, 4, out long dim) ||
                !merged.TryFloat64(row + DurationOff, out double duration) ||
                !merged.TryFloat64(row + MagnitudeOff, out double magnitude) ||
                !double.IsFinite(duration) || !double.IsFinite(magnitude)) {
                skipped.Add(id.Id);
                continue;
            }

            entries.Add(new BoostEffectEntry(id.Id, id.DisplayName, (int)dim, duration, magnitude));
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

    private static bool TryFindFirstRecord(StructInitReader.StructInit s, string firstId, out long origin) {
        var offsets = s.Bytes.Keys.Order().ToList();
        foreach (long off in offsets) {
            if (!s.TryInlineStringComplete(off, out string inline) || inline != firstId) continue;
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
