namespace EggIncognito.Artifacts.Tests;

public class ArtifactAssetsTests {
    [Fact]
    public void IconPath_IsContentRelative() =>
        Assert.Equal("_content/EggIncognito.Artifacts/images/artifacts/TUNGSTEN_ANKH/TUNGSTEN_ANKH_4.png",
            ArtifactAssets.IconPath("TUNGSTEN_ANKH", 4));

    [Fact]
    public void TargetIconPath_IsContentRelative() =>
        Assert.Equal("_content/EggIncognito.Artifacts/images/targets/none.png", ArtifactAssets.TargetIconPath("none.png"));

    [Theory]
    [InlineData("ORNATE_GUSSET", "GUSSET")]
    [InlineData("VIAL_MARTIAN_DUST", "VIAL_OF_MARTIAN_DUST")]
    [InlineData("TUNGSTEN_ANKH", "TUNGSTEN_ANKH")]
    public void KeyFor_MapsImageFolders(string name, string key) => Assert.Equal(key, ArtifactAssets.KeyFor(name));

    [Fact]
    public void EveryArtifactTier_HasAnImage() {
        string root = RepoRoot();
        var missing = ArtifactDefinitions.All
            .Where(f => f.Kind == ArtifactKind.Artifact)
            .SelectMany(f => f.Tiers.Select(t => (Key: ArtifactAssets.KeyFor(f.Name), N: t.Level + 1)))
            .Select(x => Path.Combine(root, "EggIncognito.Artifacts", "wwwroot", "images", "artifacts", x.Key,
                $"{x.Key}_{x.N}.png"))
            .Where(p => !File.Exists(p))
            .ToList();
        Assert.Empty(missing);
    }

    private static string RepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EggIncognito.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("EggIncognito.slnx not found above test output");
    }
}
