using EggIncognito.Core.Services;
using EggSharp.GameData;

namespace EggIncognito.Tests.GameData;

public class ArtifactCatalogBuilderTests {
    [Fact]
    public void Build_DecodesEveryRarityRow_AndValidates() {
        if (!AfxConfigFixture.TryLoad(out string json)) return;

        var built = ArtifactCatalogBuilder.BuildFromJson(json, "1.37");
        string doc = ArtifactCatalogBuilder.Serialize(built.File);
        GameDataProvider.Validate(ArtifactCatalog.DocumentId, doc);

        var parsed = ArtifactCatalog.Parse(doc);
        Assert.NotEmpty(parsed.Artifacts);
        Assert.Empty(built.Skipped);
        Assert.Equal("1.37", parsed.BinaryVersion);
        Assert.Equal("config", parsed.Provenance["parameters"].Origin);
        Assert.Equal("ei_afx/config", parsed.Provenance["parameters"].Locator);
        Assert.Equal("decoded", parsed.Provenance["parameters"].Method);

        var totem = parsed.Find("lunar-totem-1-common");
        Assert.NotNull(totem);
        Assert.Equal("LUNAR_TOTEM", totem.SpecName);
        Assert.Equal("INFERIOR", totem.Level);
        Assert.Equal("COMMON", totem.Rarity);
        Assert.Equal(0.7, totem.BaseQuality);
        Assert.Equal(58.66607397156087, totem.Value);
        Assert.Equal(0.9, totem.OddsMultiplier);
        Assert.Equal(10.884288412526262, totem.CraftingPrice);
        Assert.Equal(1.0884288412526262, totem.CraftingPriceLow);
        Assert.Equal(300u, totem.CraftingPriceDomain);
        Assert.Equal(0.2, totem.CraftingPriceCurve);
        Assert.Equal(1ul, totem.CraftingXp);
    }

    [Fact]
    public void Build_RaritiesOfOneTierShareBaseQuality() {
        if (!AfxConfigFixture.TryLoad(out string json)) return;

        var parsed = Parse(json);
        foreach (var tier in parsed.Artifacts.GroupBy(a => (a.SpecName, a.AfxLevel))) {
            double[] qualities = [.. tier.Select(a => a.BaseQuality).Distinct()];
            Assert.True(qualities.Length == 1,
                $"{tier.Key.SpecName} tier {tier.Key.AfxLevel} has {qualities.Length} base qualities");
        }
    }

    [Fact]
    public void Build_CraftingPriceOperandsAreUniformAcrossRows() {
        if (!AfxConfigFixture.TryLoad(out string json)) return;

        var parsed = Parse(json);
        foreach (var row in parsed.Artifacts) {
            Assert.Equal(300u, row.CraftingPriceDomain);
            Assert.Equal(0.2, row.CraftingPriceCurve);
            Assert.Equal(row.CraftingPrice / 10, row.CraftingPriceLow, 6);
        }
    }

    private static ArtifactCatalog Parse(string json) =>
        ArtifactCatalog.Parse(ArtifactCatalogBuilder.Serialize(ArtifactCatalogBuilder.BuildFromJson(json, "1.37").File));
}
