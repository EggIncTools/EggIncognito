using EggIncognito.Services.Assets;

namespace EggIncognito.Tests;

public class EggIconsTests {
    [Theory]
    [InlineData(1, "egg_edible", "Edible")]
    [InlineData(4, "egg_rocketfuel", "Rocket Fuel")]
    [InlineData(15, "egg_darkmatter", "Dark Matter")]
    [InlineData(8, "egg_crispr", "CRISPR")]
    [InlineData(16, "egg_ai", "AI")]
    [InlineData(19, "egg_enlightenment", "Enlightenment")]
    public void StandardEgg_MapsToTheCdnStemAndASpacedLabel(int egg, string stem, string label) {
        Assert.Equal("/api/v1/data/asset/icon?name=" + stem, EggIcons.Url(egg, null));
        Assert.Equal(label, EggIcons.Label(egg, null));
    }

    [Fact]
    public void ExtractedStem_OverridesTheConventionAcrossNameForms() {
        EggIcons.LoadFromJson("""
            {"eggIcons":[{"eggName":"MEDICAL","iconStem":"egg_medical2"},{"eggName":"NEBULA","iconStem":"egg_vision"},{"eggName":"ROCKET FUEL","iconStem":"egg_rocketfuel"}]}
            """);
        try {
            Assert.Equal("/api/v1/data/asset/icon?name=egg_medical2", EggIcons.Url(3, null));
            Assert.Equal("/api/v1/data/asset/icon?name=egg_vision", EggIcons.Url(17, null));
            Assert.Equal("/api/v1/data/asset/icon?name=egg_rocketfuel", EggIcons.Url(4, null));
        } finally {
            EggIcons.LoadFromJson(null);
        }
    }

    [Fact]
    public void CustomEgg_UsesTheColleggtibleIconStem() {
        Assert.Equal("/api/v1/data/asset/icon?name=pegg_ce_icon", EggIcons.Url(200, "pegg"));
        Assert.Equal("Pegg", EggIcons.Label(200, "pegg"));
    }

    [Fact]
    public void UnknownOrUnsafeEgg_HasNoIcon() {
        Assert.Null(EggIcons.Url(1000, null));
        Assert.Null(EggIcons.Url(999, null));
        Assert.Null(EggIcons.Url(200, "../evil"));
    }
}
