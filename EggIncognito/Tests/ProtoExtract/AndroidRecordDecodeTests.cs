using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class AndroidRecordDecodeTests {
    [Fact]
    public void Missions_attach_each_goal_to_its_own_record() {
        if (!CaptureFixture.TryLoad("egginc-android-1.37.so", out byte[] bin)) return;

        var missions = MissionCatalogExtractor.ExtractAuto(bin);

        Assert.True(missions.Ok, missions.Diagnostics);
        var byId = missions.Entries.ToDictionary(e => e.Id);
        Assert.Equal("Take down 25 drones.", byId["drone_takedown_30"].Goal);
        Assert.Equal("DRONE HUNTER", byId["drone_takedown_30"].DisplayName);
        Assert.Equal("Hatch 200 chickens", byId["two_hundred"].Goal);
        Assert.Equal("TWO HUNDRED", byId["two_hundred"].DisplayName);
        Assert.Equal("Earn \u001bb[s8]500 in one second", byId["earn_500s"].Goal);
    }

    [Fact]
    public void Boosts_keep_their_own_name_and_description() {
        if (!CaptureFixture.TryLoad("egginc-android-1.37.so", out byte[] bin)) return;

        var boosts = BoostCatalogExtractor.Extract(bin);

        Assert.True(boosts.Ok, boosts.Diagnostics);
        var byId = boosts.Entries.ToDictionary(e => e.Id);
        Assert.Equal("JIMBO'S EXCELLENT BIRD FEED", byId["jimbos_blue_v2"].DisplayName);
        Assert.Equal("JIMBO'S PREMIUM BIRD FEED", byId["jimbos_purple_v2"].DisplayName);
        Assert.Equal("LARGE TACHYON PRISM", byId["tachyon_prism_blue_big"].DisplayName);
        Assert.Equal("This gives you instant cash equalling the amount in the description. Not boostable.",
            byId["subsidy_application"].Description);
        Assert.Null(byId["jimbos_blue_v2"].Description);
        Assert.Contains("tachyon_prism_purple_big", byId.Keys);
    }
}
