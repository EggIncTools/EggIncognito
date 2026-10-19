using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class EventIconExtractorTests {
    [Fact]
    public void ExtractAuto_maps_every_live_event_to_an_icon_global() {
        if (!CaptureFixture.TryLoad("egginc-android-1.37.so", out byte[] bin)) return;

        var icons = EventIconExtractor.ExtractAuto(bin);

        Assert.True(icons.Ok, icons.Diagnostics);
        Assert.Equal("b_icon_drone_boost", icons.Entries.Single(e => e.EventType == "drone-boost").IconStem);
        Assert.Equal("b_icon_vehicle_sale", icons.Entries.Single(e => e.EventType == "vehicle-sale").IconStem);
        Assert.Equal("icon_piggy", icons.Entries.Single(e => e.EventType == "piggy-boost").IconStem);
        Assert.Equal("b_icon_soul_boost", icons.Entries.Single(e => e.EventType == "prestige-boost").IconStem);
        Assert.All(icons.Entries, e => Assert.Matches("^[a-z0-9_]+$", e.IconStem));
    }
}
