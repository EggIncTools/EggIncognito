using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class ResearchPriceCurveTests {
    private static readonly ResearchPriceCurve.Curve Line = new(1, [
        new(0, 0),
        new(10, 2),
        new(20, 4)
    ]);

    private static readonly ResearchPriceCurve.Constants Unit = new(2, 2, 2, 1);

    [Theory]
    [InlineData(ResearchPriceCurve.Standard)]
    [InlineData(ResearchPriceCurve.Virtue)]
    public void Build_SamplesFiveHundredPointsFromZeroFarm(int type) {
        var curve = ResearchPriceCurve.Build(type, Unit);

        Assert.Equal(500, curve.Points.Count);
        Assert.Equal(0, curve.Points[0].FarmSize);
        for (int k = 1; k < curve.Points.Count; k++) Assert.True(curve.Points[k].FarmSize > curve.Points[k - 1].FarmSize);
    }

    [Fact]
    public void Build_UnknownTypeIsEmpty() {
        Assert.Empty(ResearchPriceCurve.Build(2, Unit).Points);
    }

    [Fact]
    public void CurveQueries_InterpolateAndIntegrate() {
        Assert.Equal(3, ResearchPriceCurve.ChickenValueFromEggLevel(Line, 1.5));
        Assert.Equal(4, ResearchPriceCurve.ChickenValueFromEggLevel(Line, 7));
        Assert.Equal(15, ResearchPriceCurve.FarmSize(Line, 3));
        Assert.Equal(20, ResearchPriceCurve.FarmSize(Line, 9));
        Assert.Equal(22.5, ResearchPriceCurve.FarmValue(Line, 15));
        Assert.Equal(40, ResearchPriceCurve.FarmValue(Line, 99));
    }

    [Fact]
    public void EpicPrice_InterpolatesLinearlyAcrossLevels() {
        Assert.Equal(10, ResearchPriceCurve.EpicPrice(10, 500, 15, 0));
        Assert.Equal(500, ResearchPriceCurve.EpicPrice(10, 500, 15, 14));
    }

    [Fact]
    public void EpicPrice_FloorsAtOne() {
        Assert.Equal(1, ResearchPriceCurve.EpicPrice(0, 0, 15, 0));
    }
}
