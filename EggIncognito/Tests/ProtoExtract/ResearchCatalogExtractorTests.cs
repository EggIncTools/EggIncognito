using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class ResearchCatalogExtractorTests {
    private static bool TryLoadCapture(string file, out byte[] bin) {
        bin = [];
        string? dir = AppContext.BaseDirectory;
        while (dir is not null) {
            string candidate = Path.Combine(dir, "EggIncognito", "captures", file);
            if (File.Exists(candidate)) {
                bin = File.ReadAllBytes(candidate);
                return bin.Length > 0;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return false;
    }

    [Theory]
    [InlineData("egginc-android-1.37.so")]
    [InlineData("egginc")]
    public void Extract_ReadsIdentityAndEffects(string capture) {
        if (!TryLoadCapture(capture, out byte[] bin)) return;

        var r = ResearchCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(80, r.Entries.Count);

        var nests = Assert.Single(r.Entries, e => e.Id == "comfy_nests");
        Assert.Equal("COMFORTABLE NESTS", nests.Name);
        Assert.Equal("Increase egg laying rate by 10%", nests.Description);
        Assert.Equal(50, nests.MaxLevel);
        Assert.Equal(0, nests.Tier);

        var incubators = Assert.Single(r.Entries, e => e.Id == "better_incubators");
        Assert.NotNull(incubators.Help);
        Assert.Equal(15, incubators.MaxLevel);

        var genetics = Assert.Single(r.Entries, e => e.Id == "improved_genetics");
        Assert.Equal(2, genetics.Parts.Count);

        var silo = Assert.Single(r.Entries, e => e.Id == "silo_capacity");
        Assert.True(silo.Epic);
        Assert.Equal(20, silo.MaxLevel);
        Assert.Equal(360, Assert.Single(silo.Parts).Magnitude);

        Assert.Equal(78, r.Entries.Count(e => e.Parts.Count > 0));
    }
}
