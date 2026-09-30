using System.IO.Compression;
using System.Security.Cryptography;

namespace EggIncognito.BuildInventory;

internal sealed class ArchiveContent {
    public required string File { get; init; }
    public required int Modules { get; init; }
    public required Dictionary<string, int> Entries { get; init; }

    public int Count => Entries.Values.Sum();

    public static ArchiveContent Read(string root, string path) {
        var entries = new Dictionary<string, int>(StringComparer.Ordinal);
        using var zip = ZipFile.OpenRead(path);
        var modules = zip.Entries.Where(e => e.FullName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)).ToList();
        if (modules.Count == 0) AddApk(zip, entries);
        foreach (var module in modules) {
            using var buf = new MemoryStream(checked((int)module.Length));
            using (var es = module.Open()) es.CopyTo(buf);
            buf.Position = 0;
            using var apk = new ZipArchive(buf, ZipArchiveMode.Read);
            AddApk(apk, entries);
        }

        return new ArchiveContent { File = Path.GetRelativePath(root, path), Modules = modules.Count, Entries = entries };
    }

    public bool IsSubsetOf(ArchiveContent other) => !MissingFrom(other).Any();

    public IEnumerable<string> MissingFrom(ArchiveContent other) =>
        Entries.Where(kv => !other.Entries.TryGetValue(kv.Key, out int n) || n < kv.Value).Select(kv => kv.Key);

    private static void AddApk(ZipArchive apk, Dictionary<string, int> entries) {
        foreach (var e in apk.Entries) {
            if (e.FullName.EndsWith('/')) continue;
            using var es = e.Open();
            string key = $"{e.FullName}\t{Convert.ToHexStringLower(SHA256.HashData(es))}";
            entries[key] = entries.GetValueOrDefault(key) + 1;
        }
    }
}
