using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services.ProtoExtract;

public static partial class EggIconExtractor {
    [GeneratedRegex(@"^_ZN1A3IMG\d+(egg_[a-z0-9_]+)E$")]
    private static partial Regex IconGlobalRegex();

    public readonly record struct IconEntry(int Index, string EggName, string IconStem);

    public readonly record struct Result(bool Ok, IReadOnlyList<IconEntry> Entries, string Diagnostics);

    public static Result ExtractAuto(byte[] bin) {
        var img = BinaryImage.Load(bin);
        if (img is not ElfImage)
            return new Result(false, [],
                "egg icons require the Android ELF; iOS Mach-O is desymbolized and lambda read sites are not resolvable");

        var eggs = EggCatalogExtractor.ExtractAuto(bin);
        if (!eggs.Ok) return new Result(false, [], $"egg catalog unavailable: {eggs.Diagnostics}");

        var index = CodeIndex.Build(img);
        ulong initStart;
        if (index.TryFindFunc([EggCatalogExtractor.InitSymbol], out var range)) {
            initStart = range.Start;
        } else {
            var loc = InitArrayLocator.Create(bin);
            if (loc is null || !loc.TryLocateByString(EggCatalogExtractor.SignatureString, out ulong s, out _))
                return new Result(false, [], "eggdata init not located via symbol or signature on ELF");
            initStart = s;
        }

        var globals = img.Symbols.Where(s => s.Kind == SymbolKind.Object && IconGlobalRegex().IsMatch(s.Name)).ToList();
        if (globals.Count == 0) return new Result(false, [], "no A::IMG::egg_* globals found");

        var sites = new List<(ulong Va, string Stem)>();
        var scans = Arm64DataXrefScanner.ScanMany(bin, [.. globals.Select(g => g.Value)]);
        foreach (var g in globals) {
            string stem = IconGlobalRegex().Match(g.Name).Groups[1].Value;
            if (!scans.TryGetValue(g.Value, out var scan)) continue;
            foreach (var site in scan.Sites) {
                if (site.Access != "read" || site.Symbol.Length != 0 || site.Va <= initStart) continue;
                if (!IsIconLambda(bin, img, site.Va)) continue;
                sites.Add((site.Va, stem));
            }
        }

        sites.Sort((a, b) => a.Va.CompareTo(b.Va));
        var clusters = Clusters(sites).Select(Unique).ToList();
        var run = clusters.Where(c => c.Count == eggs.Entries.Count).ToList();
        if (run.Count != 1)
            return new Result(false, [],
                $"{sites.Count} egg icon read sites in clusters [{string.Join(", ", clusters.Select(c => c.Count))}] after dropping shared stems, expected one cluster of {eggs.Entries.Count} ({globals.Count} A::IMG::egg_* globals)");

        var entries = new List<IconEntry>(run[0].Count);
        for (int i = 0; i < run[0].Count; i++)
            entries.Add(new IconEntry(i, eggs.Entries[i].Name ?? "", run[0][i].Stem));

        return new Result(true, entries, $"{entries.Count} egg icons");
    }

    private static List<(ulong Va, string Stem)> Unique(List<(ulong Va, string Stem)> cluster) {
        var counts = cluster.GroupBy(s => s.Stem, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return [.. cluster.Where(s => counts[s.Stem] == 1)];
    }

    private const ulong ClusterGap = 0x1000;

    private static List<List<(ulong Va, string Stem)>> Clusters(List<(ulong Va, string Stem)> sorted) {
        var outp = new List<List<(ulong Va, string Stem)>>();
        foreach (var site in sorted) {
            if (outp.Count == 0 || site.Va - outp[^1][^1].Va > ClusterGap) outp.Add([]);
            outp[^1].Add(site);
        }

        return outp;
    }

    private const uint LdrX0FromReg = 0xF9400000;
    private const uint LdrRnMask = 0x3E0;
    private const uint Ret = 0xD65F03C0;

    private static bool IsIconLambda(byte[] bin, IBinaryImage img, ulong adrpVa) {
        return Word(bin, img, adrpVa + 8) is { } load && (load & ~LdrRnMask) == LdrX0FromReg
               && Word(bin, img, adrpVa + 12) == Ret;
    }

    private static uint? Word(byte[] bin, IBinaryImage img, ulong va) {
        if (!img.TryVaToFileOffset(va, out int off, out _) || off < 0 || off + 4 > bin.Length) return null;
        return BinaryPrimitives.ReadUInt32LittleEndian(bin.AsSpan(off, 4));
    }
}
