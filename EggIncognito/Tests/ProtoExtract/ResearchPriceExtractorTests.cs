using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class ResearchPriceExtractorTests {
    private static bool TryLoadCapture(string file, out byte[] bin) {
        bin = [];
        string? dir = AppContext.BaseDirectory;
        while (dir is not null) {
            string candidate = Path.Combine(dir, "EggIncognito", "captures", file);
            if (File.Exists(candidate)) {
                bin = File.ReadAllBytes(candidate);
                return bin.Length > 0;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return false;
    }

    private static ResearchPriceExtractor.ByIdResult ExtractPrices(byte[] bin) {
        var catalog = ResearchCatalogExtractor.Extract(bin);
        Assert.True(catalog.Ok, catalog.Diagnostics);
        return ResearchPriceExtractor.ExtractById(bin, catalog.Entries);
    }

    [Theory]
    [InlineData("egginc")]
    [InlineData("egginc-android-1.37.so")]
    public void Price_ComfyNestsLevelZeroCostsHalfABock(string capture) {
        if (!TryLoadCapture(capture, out byte[] bin)) return;

        var curve = ResearchPriceCurveExtractor.Extract(bin);
        Assert.True(curve.Ok, curve.Diagnostics);
        var prices = ExtractPrices(bin);
        Assert.True(prices.Ok, prices.Diagnostics);

        var standard = ResearchPriceCurve.Build(ResearchPriceCurve.Standard, curve.Constants);
        var nests = prices.Prices["comfy_nests"];
        double level0 = ResearchPriceCurve.CommonPrice(nests.PriceStart, nests.PriceEnd, nests.PricePercent,
            nests.MaxLevel, 0, standard);
        Assert.Equal(0.5, level0, 0.01);
    }

    [Theory]
    [InlineData("egginc")]
    [InlineData("egginc-android-1.37.so")]
    public void Price_HoldToHatchLevelZeroCostsTenEggs(string capture) {
        if (!TryLoadCapture(capture, out byte[] bin)) return;

        var prices = ExtractPrices(bin);
        Assert.True(prices.Ok, prices.Diagnostics);
        var hatch = prices.Prices["hold_to_hatch"];
        Assert.True(hatch.Epic);
        Assert.Equal(10, ResearchPriceCurve.EpicPrice(hatch.EpicPriceStart, hatch.EpicPriceEnd, hatch.MaxLevel, 0));
    }

    [Theory]
    [InlineData("egginc")]
    [InlineData("egginc-android-1.37.so")]
    public void Extract_ReadsComfyNestsPriceFields(string capture) {
        if (!TryLoadCapture(capture, out byte[] bin)) return;

        var r = ExtractPrices(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(80, r.Prices.Count);

        var nests = r.Prices["comfy_nests"];
        Assert.False(nests.Epic);
        Assert.Equal(50, nests.MaxLevel);
        Assert.Equal(0.7, nests.PricePercent, 12);
        Assert.Equal(0, nests.EpicPriceStart);
        Assert.Equal(0, nests.EpicPriceEnd);
        Assert.True(nests.PriceEnd > nests.PriceStart);

        var silo = r.Prices["silo_capacity"];
        Assert.True(silo.Epic);
        Assert.Equal(20, silo.MaxLevel);
        Assert.True(silo.EpicPriceEnd > silo.EpicPriceStart);
    }

    [Fact]
    public void Extract_AgreesAcrossPlatforms() {
        if (!TryLoadCapture("egginc", out byte[] apple)) return;
        if (!TryLoadCapture("egginc-android-1.37.so", out byte[] ndk)) return;

        var a = ExtractPrices(apple);
        var n = ExtractPrices(ndk);
        Assert.True(a.Ok, a.Diagnostics);
        Assert.True(n.Ok, n.Diagnostics);
        Assert.Equal(80, a.Prices.Count);
        Assert.Equal(a.Prices.Keys.Order(StringComparer.Ordinal), n.Prices.Keys.Order(StringComparer.Ordinal));
        Assert.All(a.Prices, kv => Assert.Equal(kv.Value, n.Prices[kv.Key]));
    }
}
