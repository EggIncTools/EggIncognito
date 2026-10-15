using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class EggIconExtractorTests {
    [Fact]
    public void ExtractAuto_matches_egg_catalog_count() {
        if (!CaptureFixture.TryLoad("egginc-android-1.37.so", out byte[] bin)) return;

        var eggs = EggCatalogExtractor.ExtractAuto(bin);
        var icons = EggIconExtractor.ExtractAuto(bin);

        Assert.True(icons.Ok, icons.Diagnostics);
        Assert.Equal(eggs.Entries.Count, icons.Entries.Count);
        Assert.All(icons.Entries, e => Assert.StartsWith("egg_", e.IconStem));
    }
}
