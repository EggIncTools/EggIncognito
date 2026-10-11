using EggIncognito.Core.Services;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.GameData;

namespace EggIncognito.Tests.ProtoExtract;

public class BoostEffectExtractorTests {
    private static bool TryLoadMacho(out byte[] bin) {
        bin = [];
        string? dir = AppContext.BaseDirectory;
        while (dir is not null) {
            string candidate = Path.Combine(dir, "EggIncognito", "captures", "egginc");
            if (File.Exists(candidate)) {
                bin = File.ReadAllBytes(candidate);
                return bin.Length > 0;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return false;
    }

    [Fact]
    public void Extract_DecodesAnchoredRecords_AndBuildsValidDocument() {
        if (!TryLoadMacho(out byte[] bin)) return;

        var r = BoostEffectExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.True(r.Entries.Count >= 14, r.Diagnostics);

        var blue = Assert.Single(r.Entries, e => e.Id == "jimbos_blue_v2");
        Assert.Equal(2, blue.Dimension);
        Assert.Equal(1200, blue.DurationSeconds);
        Assert.Equal(3, blue.Magnitude);

        var orange = Assert.Single(r.Entries, e => e.Id == "tachyon_prism_orange");
        Assert.Equal(4, orange.Dimension);
        Assert.Equal(600, orange.DurationSeconds);
        Assert.Equal(1000, orange.Magnitude);

        var doc = GameDataDocBuilders.BuildBoosts(r.Entries, "test");
        Assert.True(doc.Skipped.Count == 0, string.Join("; ", doc.Skipped));
        GameDataProvider.Validate("boosts", doc.Json);
        var parsed = EffectDataLoader.Parse(doc.Json);
        var row = Assert.Single(parsed.Rows, x => x.Id == "tachyon_prism_orange");
        Assert.Equal(EffectTarget.IHR, row.Target);
        Assert.Equal(CombineMode.Mul, row.CombineMode);
        Assert.Equal(1000, row.Magnitude);
        var meta = Assert.IsAssignableFrom<IReadOnlyDictionary<string, System.Text.Json.JsonElement>>(row.Meta);
        Assert.Equal(600, meta["durationSeconds"].GetInt64());
    }
}
