using EggIncognito.Core.Services;

namespace EggIncognito.GameData.Tests;

public sealed class ArtifactCatalogCraftingLevelsTests {
    [Fact]
    public void CraftingLevels_RoundTripThroughTheDocument() {
        var file = new ArtifactCatalogBuilder.ArtifactCatalogBuildFile(
            [
                new ArtifactCatalogBuilder.ArtifactCatalogBuildRow("lunar-totem-1-common", "LUNAR_TOTEM", "INFERIOR", "COMMON",
                    0, 0, 0, 1, 0.7, 0.9, 58.6, 10.8, 1.08, 0.2, 300, 1)
            ],
            [
                new ArtifactCatalogBuilder.ArtifactCraftingLevelBuildRow(1, 0, 1),
                new ArtifactCatalogBuilder.ArtifactCraftingLevelBuildRow(2, 500, 1.25),
                new ArtifactCatalogBuilder.ArtifactCraftingLevelBuildRow(3, 2000, 1.5)
            ],
            "test-1.0",
            new Dictionary<string, EggIncognito.Core.Services.ProvenanceSource>());

        var parsed = ArtifactCatalog.Parse(ArtifactCatalogBuilder.Serialize(file));

        Assert.Equal(3, parsed.CraftingLevels.Count);
        Assert.Equal(new[] { 1.0, 1.25, 1.5 }, parsed.CraftingLevels.Select(l => l.RarityMult));
        Assert.Equal(2000, parsed.CraftingLevels[2].XpRequired);
    }
}
