using System.Buffers;
using System.IO.Compression;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class RpoAssetExtractor {
    private const long MaxEntryBytes = 50_000_000L;
    private static readonly SearchValues<char> PathSeparators = SearchValues.Create("/\\");

    public static ExtractResult Extract(byte[] archiveZipBytes) {
        if (archiveZipBytes is null || archiveZipBytes.Length == 0)
            return new ExtractResult(false, [], "empty archive");

        ZipArchive zip;
        try {
            zip = new ZipArchive(new MemoryStream(archiveZipBytes, false), ZipArchiveMode.Read);
        } catch {
            var single = RpoMeshDecoder.Decode(archiveZipBytes);
            return single.Ok
                ? new ExtractResult(true, [new Asset("mesh", "<raw>", single)], "ok")
                : new ExtractResult(false, [], "not a zip and not a decodable mesh");
        }

        var entries = new List<(string Name, byte[] Bytes)>();
        using (zip) {
            foreach (var entry in zip.Entries) {
                if (!IsRpoEntry(entry.FullName)) continue;
                if (entry.Length is <= 0 or > MaxEntryBytes) continue;
                try {
                    using var es = entry.Open();
                    using var buf = new MemoryStream();
                    es.CopyTo(buf);
                    entries.Add((entry.FullName, buf.ToArray()));
                } catch {
                    /* skip unreadable entry */
                }
            }
        }

        return DecodeEntries(entries);
    }

    public static ExtractResult FromEntries(IEnumerable<(string Name, byte[] Bytes)> entries) =>
        DecodeEntries(entries.Where(e => IsRpoEntry(e.Name) && e.Bytes.LongLength is > 0 and <= MaxEntryBytes));

    private static ExtractResult DecodeEntries(IEnumerable<(string Name, byte[] Bytes)> entries) {
        var assets = new List<Asset>();
        foreach ((string name, byte[] bytes) in entries) {
            string key = KeyFromEntry(name);
            assets.Add(new Asset(key, name, RpoMeshDecoder.Decode(bytes, key)));
        }

        bool ok = assets.Any(a => a.Decode.Ok);
        return new ExtractResult(ok, assets,
            ok ? "ok" : assets.Count == 0 ? "no .rpo/.rpoz entries found" : "found meshes but none decoded");
    }

    private static bool IsRpoEntry(string fullName) =>
        fullName.EndsWith(".rpo", StringComparison.OrdinalIgnoreCase)
        || fullName.EndsWith(".rpoz", StringComparison.OrdinalIgnoreCase);

    private static string KeyFromEntry(string fullName) {
        int slash = fullName.LastIndexOfAny(PathSeparators);
        string name = slash >= 0 ? fullName[(slash + 1)..] : fullName;
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    public sealed record Asset(string Key, string SourceEntry, RpoMeshDecoder.DecodeResult Decode);

    public sealed record ExtractResult(bool Ok, IReadOnlyList<Asset> Assets, string Diagnostics);
}
