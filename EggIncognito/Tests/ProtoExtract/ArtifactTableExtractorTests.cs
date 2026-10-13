using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class ArtifactTableExtractorTests {
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

    [Fact]
    public void Extract_ReadsStoneSlotAnchors() {
        if (!TryLoadCapture("egginc-android-1.37.so", out byte[] bin)) return;

        var r = ArtifactTableExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        var lunar = Assert.Single(r.Families, f => f.BinaryId == "lunar_totem");
        Assert.Equal(0, Slots(lunar, 0, 0));
        Assert.Equal(0, Slots(lunar, 3, 0));
        Assert.Equal(3, Slots(lunar, 3, 3));
        Assert.All(r.Families.SelectMany(f => f.Tiers).SelectMany(t => t.Effects),
            e => Assert.InRange(e.Slots, 0, 3));
    }

    private static int Slots(ArtifactTableExtractor.Family f, int level, int rarity) =>
        Assert.Single(Assert.Single(f.Tiers, t => t.Level == level).Effects, e => e.Rarity == rarity).Slots;
}
