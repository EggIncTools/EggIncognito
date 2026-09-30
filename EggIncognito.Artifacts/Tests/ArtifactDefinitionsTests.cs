namespace EggIncognito.Artifacts.Tests;

public class ArtifactDefinitionsTests {
    [Fact]
    public void Snapshot_HasEveryFamily() => Assert.Equal(44, ArtifactDefinitions.All.Count);

    [Fact]
    public void TierName_ReadsBinaryName() =>
        Assert.Equal("BASIC LUNAR TOTEM", ArtifactDefinitions.TierName("LUNAR_TOTEM", 0));

    [Fact]
    public void Kind_ClassifiesStones() => Assert.Equal(ArtifactKind.Stone, ArtifactDefinitions.Kind("SOUL_STONE"));

    [Fact]
    public void Find_KeepsBinaryIdSpelling() =>
        Assert.Equal("tau_centi_geode", ArtifactDefinitions.Find("TAU_CETI_GEODE")?.BinaryId);

    [Fact]
    public void FragmentFamily_ResolvesStoneFromRecipe() =>
        Assert.Equal("SOUL_STONE", ArtifactDefinitions.FragmentFamily("SOUL_STONE_FRAGMENT"));

    [Fact]
    public void DimensionLabel_ReadsNameStr() => Assert.Equal("away earnings", ArtifactDefinitions.DimensionLabel(3));

    [Fact]
    public void EffectText_RendersGenericTemplateFromMagnitude() {
        Assert.Equal("+100% away earnings", ArtifactDefinitions.EffectText("LUNAR_TOTEM", 0, 0));
        Assert.Equal("8× away earnings", ArtifactDefinitions.EffectText("LUNAR_TOTEM", 1, 1));
        Assert.Equal("-5% research cost", ArtifactDefinitions.EffectText("PUZZLE_CUBE", 0, 0));
        Assert.Equal("+10% drone frequency", ArtifactDefinitions.EffectText("NEODYMIUM_MEDALLION", 0, 0));
    }

    [Fact]
    public void EffectText_RendersPerRarityTemplates() {
        Assert.Equal("+0.25% to Egg of Prophecy bonus", ArtifactDefinitions.EffectText("BOOK_OF_BASAN", 0, 0));
        Assert.Equal("Gold gifts and drone rewards guaranteed!", ArtifactDefinitions.EffectText("BEAK_OF_MIDAS", 3, 3));
        Assert.Equal("cash drone rewards and gifts guaranteed.",
            ArtifactDefinitions.EffectText("CARVED_RAINSTICK", 3, 3));
    }

    [Fact]
    public void EffectMarkup_KeepsColourEscapes() =>
        Assert.Equal("\u001bg+100%\u001bw away earnings", ArtifactDefinitions.EffectMarkup("LUNAR_TOTEM", 0, 0));

    [Fact]
    public void Effect_IsNullForUnknownRarity() => Assert.Null(ArtifactDefinitions.Effect("LUNAR_TOTEM", 0, 3));

    [Fact]
    public void Description_ReadsFamilyLambda() =>
        Assert.Equal("Modify away earnings", ArtifactDefinitions.Find("LUNAR_TOTEM")?.Description);

    [Fact]
    public void EveryArtifactAndStoneTier_HasEveryRarityEffect() {
        foreach (var f in ArtifactDefinitions.All.Where(f => f.Kind is ArtifactKind.Artifact or ArtifactKind.Stone)) {
            foreach (var t in f.Tiers) {
                Assert.Equal(t.RarityCount, t.Effects.Count);
                foreach (var e in t.Effects) Assert.NotNull(ArtifactDefinitions.EffectText(f.Name, t.Level, e.Rarity));
            }
        }
    }

    [Fact]
    public void Fingerprint_RoundTripsThroughParse() {
        var parsed = ArtifactDefinitions.Parse(
            """{"families":[{"name":"LUNAR_TOTEM","afxId":0,"binaryId":"lunar_totem","pluralName":"LUNAR TOTEMS","kind":0,"dimension":3,"order":0,"description":"","tiers":[]}],"dimensionLabels":{"3":"away earnings"},"effectTemplate":"","binaryVersion":"1.0"}""");
        var altered = ArtifactDefinitions.Parse(
            """{"families":[{"name":"LUNAR_TOTEM","afxId":0,"binaryId":"lunar_totem","pluralName":"LUNAR TOTEMS","kind":0,"dimension":4,"order":0,"description":"","tiers":[]}],"dimensionLabels":{"3":"away earnings"},"effectTemplate":"","binaryVersion":"1.0"}""");
        Assert.Equal(ArtifactDefinitions.Fingerprint(parsed.Families, parsed.Labels),
            ArtifactDefinitions.Fingerprint(parsed.Families, parsed.Labels));
        Assert.NotEqual(ArtifactDefinitions.Fingerprint(parsed.Families, parsed.Labels),
            ArtifactDefinitions.Fingerprint(altered.Families, altered.Labels));
    }
}
