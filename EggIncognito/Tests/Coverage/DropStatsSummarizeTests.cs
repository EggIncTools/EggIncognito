using EggIncognito.Models.Observations;
using EggIncognito.Services.Coverage;
using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Coverage;

public sealed class DropStatsSummarizeTests {
    private static ArtifactByproductRow Frag(int n) => new("LUNAR_STONE_FRAGMENT", "INFERIOR", "COMMON", n);

    [Fact]
    public void Mean_CountsConsumesWithoutTheDrop() {
        var s = DropStats.Summarize("LUNAR_TOTEM", "GREATER", "COMMON", [[Frag(3)], [Frag(1)], []]);
        var row = Assert.Single(s.Rows);
        Assert.Equal(4.0 / 3, row.Mean, 6);
        Assert.Equal(2.0 / 3, row.Share, 6);
        Assert.Equal((0, 3), (row.Min, row.Max));
        Assert.Equal([0, 1, 3], row.Buckets.Select(b => b.Count));
    }

    [Fact]
    public void Gold_ReportsChanceAndDistributionIncludingMisses() {
        var g = DropStats.SummarizeGold([0, 0, 2, 2]);
        Assert.NotNull(g);
        Assert.Equal(0.5, g.Share, 6);
        Assert.Equal(1.0, g.Mean, 6);
        Assert.Equal([0.0, 2.0], g.Buckets.Select(b => b.Amount));
    }

    [Fact]
    public void Gold_NeverDroppedIsNull() => Assert.Null(DropStats.SummarizeGold([0, 0]));

    [Fact]
    public void MalformedRowCountsAsEmptyConsume() {
        var parsed = ArtifactObservationQuery.ParseByproducts("{not json");
        var s = DropStats.Summarize("LUNAR_TOTEM", "GREATER", "COMMON", [[Frag(2)], parsed]);
        Assert.Equal(1.0, Assert.Single(s.Rows).Mean, 6);
    }
}
