using System.Buffers;
using System.IO.Compression;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class RpoAssetLister {
    private const long MaxEntryBytes = 50_000_000L;
    private static readonly SearchValues<char> PathSeparators = SearchValues.Create("/\\");

    public static IReadOnlyList<string> ListStems(byte[] archiveZipBytes) {
        var stems = new SortedSet<string>(StringComparer.Ordinal);
        ForEachRpoEntry(archiveZipBytes, (stem, _) => {
            stems.Add(stem);
            return false;
        });
        return stems.ToList();
    }

    public static byte[]? ReadStem(byte[] archiveZipBytes, string stem) {
        byte[]? found = null;
        ForEachRpoEntry(archiveZipBytes, (s, read) => {
            if (!string.Equals(s, stem, StringComparison.Ordinal)) return false;
            found = read();
            return true;
        });
        return found;
    }

    private static void ForEachRpoEntry(byte[] zipBytes, Func<string, Func<byte[]>, bool> visit) {
        if (zipBytes is null || zipBytes.Length == 0) return;
        ZipArchive zip;
        try {
            zip = new ZipArchive(new MemoryStream(zipBytes, false), ZipArchiveMode.Read);
        } catch {
            return;
        }

        using (zip) {
            foreach (var entry in zip.Entries) {
                string name = entry.FullName;
                if (!name.EndsWith(".rpo", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith(".rpoz", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                if (entry.Length is <= 0 or > MaxEntryBytes) continue;

                string stem = Stem(name);

                byte[] Read() {
                    using var es = entry.Open();
                    using var buf = new MemoryStream();
                    es.CopyTo(buf);
                    return buf.ToArray();
                }

                if (visit(stem, Read)) return;
            }
        }
    }

    private static string Stem(string fullName) {
        int slash = fullName.LastIndexOfAny(PathSeparators);
        string name = slash >= 0 ? fullName[(slash + 1)..] : fullName;
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
