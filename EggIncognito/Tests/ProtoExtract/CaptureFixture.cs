using System.Collections.Concurrent;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public static class CaptureFixture {
    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> Bins = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<ResearchCatalogExtractor.Result>> Catalogs = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<ResearchPriceExtractor.ByIdResult>> Prices = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<BoostEffectExtractor.Result>> Boosts = new(StringComparer.Ordinal);

    public static bool TryLoad(string file, out byte[] bin) {
        bin = Bins.GetOrAdd(file, f => new Lazy<byte[]>(() => Locate(f))).Value;
        return bin.Length > 0;
    }

    public static ResearchCatalogExtractor.Result ResearchCatalog(string capture, byte[] bin) =>
        Catalogs.GetOrAdd(capture, _ => new Lazy<ResearchCatalogExtractor.Result>(() => ResearchCatalogExtractor.Extract(bin))).Value;

    public static ResearchPriceExtractor.ByIdResult ResearchPrices(string capture, byte[] bin) =>
        Prices.GetOrAdd(capture, _ => new Lazy<ResearchPriceExtractor.ByIdResult>(
            () => ResearchPriceExtractor.ExtractById(bin, ResearchCatalog(capture, bin).Entries))).Value;

    public static BoostEffectExtractor.Result BoostTable(string capture, byte[] bin) =>
        Boosts.GetOrAdd(capture, _ => new Lazy<BoostEffectExtractor.Result>(() => BoostEffectExtractor.Extract(bin))).Value;

    private static byte[] Locate(string file) {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null) {
            string candidate = Path.Combine(dir, "EggIncognito", "captures", file);
            if (File.Exists(candidate)) return File.ReadAllBytes(candidate);
            dir = Path.GetDirectoryName(dir);
        }

        return [];
    }
}
