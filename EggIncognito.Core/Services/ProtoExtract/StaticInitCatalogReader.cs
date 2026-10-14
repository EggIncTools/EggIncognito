using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class StaticInitCatalogReader {
    public static Result Read(byte[] bin, string initSymbol, Func<string, bool> isId) {
        var img = BinaryImage.Load(bin);
        return ReadWith(bin, img?.Symbols ?? [], initSymbol, isId);
    }

    public static Result ReadWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms,
        string initSymbol, Func<string, bool> isId) {
        var scan = Arm64DataTableReader.ScanWith(bin, syms, [initSymbol]);
        return !scan.Ok ? new Result(false, [], scan.Diagnostics) : Classify(bin, scan.Addresses, isId);
    }

    public static Result ReadRange(byte[] bin, ulong startVa, ulong endVa, Func<string, bool> isId) {
        var scan = Arm64DataTableReader.ScanRange(bin, startVa, endVa);
        return !scan.Ok ? new Result(false, [], scan.Diagnostics) : Classify(bin, scan.Addresses, isId);
    }

    private static Result Classify(byte[] bin, IReadOnlyList<Arm64DataTableReader.AddressRef> addresses,
        Func<string, bool> isId) {
        var img = BinaryImage.Load(bin);
        var refs = new List<(ulong Va, string Str)>();
        foreach (var a in addresses) {
            if (!BinaryStrings.IsStringSection(a.Section)) continue;
            refs.Add((a.Va, BinaryStrings.ReadCstr(bin, img, a.Va)));
        }

        var kept = new List<(ulong Va, string Str)>();
        foreach (var c in refs) {
            if (c.Str.Length == 0) continue;
            if (kept.Count > 0) {
                (ulong Va, string Str) = kept[^1];
                ulong pEnd = Va + (ulong)Encoding.UTF8.GetByteCount(Str) + 1;
                if (c.Va > Va && c.Va < pEnd && Str.EndsWith(c.Str, StringComparison.Ordinal))
                    continue;
            }

            kept.Add(c);
        }

        var entries = new List<(string Id, string? Name, string? Desc)>();
        foreach ((_, string Str) in kept) {
            if (isId(Str)) {
                entries.Add((Str, null, null));
                continue;
            }

            if (entries.Count == 0) continue;

            var cur = entries[^1];
            if (TryDescription(Str, out string desc))
                entries[^1] = cur with { Desc = cur.Desc ?? desc };
            else if (cur.Name is null && LooksLikeDisplayName(Str))
                entries[^1] = cur with { Name = Str };
        }

        var outp = entries
            .Where(e => !IsSuffixFragment(e, entries))
            .Select(e => new Entry(e.Id, e.Name, e.Desc))
            .ToList();
        return new Result(true, outp, $"{outp.Count} entries");
    }

    private static bool IsSuffixFragment((string Id, string? Name, string? Desc) e,
        IReadOnlyList<(string Id, string? Name, string? Desc)> all) {
        if (e.Name is not null || e.Desc is not null) return false;
        foreach (var o in all) {
            if (o.Id.Length > e.Id.Length && o.Id.EndsWith(e.Id, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool LooksLikeDisplayName(string s)
        => s.Length > 0 && (s.Contains(' ') || char.IsUpper(s[0]) || char.IsDigit(s[0]));

    private static bool TryDescription(string s, out string desc) {
        desc = "";
        if (s.Length < 2 || s[0] != '\x1b') return false;
        string body = s[1] == 'z' ? s[2..] : s[1..];
        if (body.Length == 0) return false;
        desc = body;
        return true;
    }

    public readonly record struct Entry(string Id, string? DisplayName, string? Description);

    public readonly record struct Result(bool Ok, IReadOnlyList<Entry> Entries, string Diagnostics);
}
