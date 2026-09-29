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
    public void Fingerprint_RoundTripsThroughParse() {
        var parsed = ArtifactDefinitions.Parse(
            """{"families":[{"name":"LUNAR_TOTEM","afxId":0,"binaryId":"lunar_totem","pluralName":"LUNAR TOTEMS","kind":0,"dimension":3,"order":0,"tiers":[]}],"dimensionLabels":{"3":"away earnings"},"binaryVersion":"1.0"}""");
        var altered = ArtifactDefinitions.Parse(
            """{"families":[{"name":"LUNAR_TOTEM","afxId":0,"binaryId":"lunar_totem","pluralName":"LUNAR TOTEMS","kind":0,"dimension":4,"order":0,"tiers":[]}],"dimensionLabels":{"3":"away earnings"},"binaryVersion":"1.0"}""");
        Assert.Equal(ArtifactDefinitions.Fingerprint(parsed.Families, parsed.Labels),
            ArtifactDefinitions.Fingerprint(parsed.Families, parsed.Labels));
        Assert.NotEqual(ArtifactDefinitions.Fingerprint(parsed.Families, parsed.Labels),
            ArtifactDefinitions.Fingerprint(altered.Families, altered.Labels));
    }
}
