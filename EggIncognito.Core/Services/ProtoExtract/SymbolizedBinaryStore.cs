using System.IO.Compression;

namespace EggIncognito.Core.Services.ProtoExtract;

public sealed class SymbolizedBinaryStore(string ipaDir, Func<byte[], bool>? isSymbolized = null) {
    private readonly Func<byte[], bool> _isSymbolized = isSymbolized ?? (b => MachoSymbols.Read(b).Count > 50_000);

    public IReadOnlyList<string> ListVersions()
        => BuildIndex().Keys.OrderByDescending(ProtoVersionQuality.DottedVersionKey).ToList();

    public Result Get(string? version) {
        var index = BuildIndex();
        if (index.Count == 0) {
            return new Result(false, null, "", false,
                $"no symbolized binary available; add a symbolized .ipa to {ipaDir}");
        }

        if (!string.IsNullOrEmpty(version) && index.TryGetValue(version, out byte[]? exact))
            return new Result(true, exact, version, true, "ok");

        string newest = index.Keys.OrderByDescending(ProtoVersionQuality.DottedVersionKey).First();
        string note = string.IsNullOrEmpty(version)
            ? "no version requested; using newest symbolized build"
            : $"no symbolized build for {version}; using newest ({newest})";
        return new Result(true, index[newest], newest, false, note);
    }

    private Dictionary<string, byte[]> BuildIndex() {
        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (!Directory.Exists(ipaDir)) return map;
        foreach (string path in Directory.EnumerateFiles(ipaDir, "*.ipa")) {
            try {
                using var zip = ZipFile.OpenRead(path);
                (string? version, byte[]? exec) = SymbolizedIpa.Read(zip);
                if (version is null || exec is null) continue;
                if (!_isSymbolized(exec)) continue;
                map.TryAdd(version, exec);
            } catch {
                /* skip malformed ipa */
            }
        }

        return map;
    }

    public readonly record struct Result(bool Ok, byte[]? Bytes, string Version, bool ExactVersion, string Diagnostics);
}
