using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;
using Ei;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Tests.Devices;

public class RareCraftDismisserTests {
    [Theory]
    [InlineData(ArtifactSpec.Types.Rarity.Rare)]
    [InlineData(ArtifactSpec.Types.Rarity.Epic)]
    [InlineData(ArtifactSpec.Types.Rarity.Legendary)]
    public void RarityOf_ReportsAnyRarityAboveCommon(ArtifactSpec.Types.Rarity rarity) {
        var flow = Craft(new CraftArtifactResponse { ItemId = 991, RarityAchieved = rarity });
        Assert.Equal(rarity, RareCraftDismisser.RarityOf(flow));
    }

    [Fact]
    public void RarityOf_IgnoresCommonCrafts() {
        var flow = Craft(new CraftArtifactResponse { ItemId = 991, RarityAchieved = ArtifactSpec.Types.Rarity.Common });
        Assert.Null(RareCraftDismisser.RarityOf(flow));
    }

    [Fact]
    public void RarityOf_IgnoresFailedCrafts() {
        var flow = Craft(new CraftArtifactResponse { ItemId = 0, RarityAchieved = ArtifactSpec.Types.Rarity.Legendary });
        Assert.Null(RareCraftDismisser.RarityOf(flow));
    }

    [Fact]
    public void RarityOf_IgnoresOtherRoutes() {
        var flow = Craft(new CraftArtifactResponse { ItemId = 1, RarityAchieved = ArtifactSpec.Types.Rarity.Epic })
            with { Path = "ei_afx/consume_artifact" };
        Assert.Null(RareCraftDismisser.RarityOf(flow));
    }

    [Fact]
    public void RarityOf_IgnoresMissingOrBrokenResponses() {
        var flow = Craft(new CraftArtifactResponse { ItemId = 1, RarityAchieved = ArtifactSpec.Types.Rarity.Epic });
        Assert.Null(RareCraftDismisser.RarityOf(flow with { ResponseJsonRaw = null }));
        Assert.Null(RareCraftDismisser.RarityOf(flow with { ResponseJsonRaw = "{not json" }));
    }

    [Fact]
    public void PointFor_ScalesNormalizedCoordinatesToTheScreen() {
        var (x, y) = RareCraftDismisser.PointFor(new UiScreenSize(1080, 2340), 0.5, 0.49);
        Assert.Equal(540, x);
        Assert.Equal(1147, y);
    }

    [Fact]
    public void PointFor_ClampsOutOfRangeCoordinates() {
        var (x, y) = RareCraftDismisser.PointFor(new UiScreenSize(1080, 2340), -1, 7);
        Assert.Equal(0, x);
        Assert.Equal(2340, y);
    }

    [Fact]
    public void Config_BindsTheCraftSectionWithDefaults() {
        var empty = DeviceCaptureConfig.Bind(new ConfigurationBuilder().Build());
        Assert.True(empty.CraftAutoDismiss);
        Assert.Equal(2500, empty.CraftDismissDelayMs);
        Assert.Equal(0.5, empty.CraftDismissX);
        Assert.Equal(0.49, empty.CraftDismissY);

        var bound = DeviceCaptureConfig.Bind(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> {
                ["DeviceCapture:Craft:AutoDismiss"] = "false",
                ["DeviceCapture:Craft:DismissDelayMs"] = "900",
                ["DeviceCapture:Craft:DismissX"] = "0.25",
                ["DeviceCapture:Craft:DismissY"] = "0.75"
            }).Build());
        Assert.False(bound.CraftAutoDismiss);
        Assert.Equal(900, bound.CraftDismissDelayMs);
        Assert.Equal(0.25, bound.CraftDismissX);
        Assert.Equal(0.75, bound.CraftDismissY);
    }

    [Fact]
    public void CompositeFlowObserver_FansOutToEveryObserver() {
        var seen = new List<string>();
        var composite = new CompositeFlowObserver([new Probe(seen, "a"), new Probe(seen, "b")]);

        composite.OnFlowProcessed("d1", Craft(new CraftArtifactResponse()));

        Assert.Equal(2, composite.Count);
        Assert.Equal(["a:d1", "b:d1"], seen);
    }

    private sealed class Probe(List<string> seen, string name) : IProcessedFlowObserver {
        public void OnFlowProcessed(string deviceId, DashboardFlow flow) => seen.Add($"{name}:{deviceId}");
    }

    private static DashboardFlow Craft(CraftArtifactResponse response) =>
        new(0, "", RareCraftDismisser.CraftRoute, "POST", 200,
            null, null, "", null,
            RequestJsonRaw: JsonFormatter.Default.Format(new CraftArtifactRequest()),
            ResponseJsonRaw: JsonFormatter.Default.Format(response));
}
