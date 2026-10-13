using EggIncognito.Core.Services;
using EggSharp.GameData;

namespace EggIncognito.Tests.ProtoExtract;

public class BoostEffectExtractorTests {
    private static bool TryLoadCapture(string file, out byte[] bin) => CaptureFixture.TryLoad(file, out bin);

    [Theory]
    [InlineData("egginc-android-1.37.so")]
    [InlineData("egginc")]
    public void Extract_DecodesAnchoredRecords_AndBuildsValidDocument(string capture) {
        if (!TryLoadCapture(capture, out byte[] bin)) return;

        var r = CaptureFixture.BoostTable(capture, bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(33, r.Entries.Count);
        Assert.Contains(r.Entries, e => e.Id == "quantum_bulb" && e.DurationSeconds == 600 && e.Magnitude == 1);
        Assert.Contains(r.Entries, e => e.Id == "blank_check" && e.DurationSeconds == 0 && e.Magnitude == 1);
        Assert.Contains(r.Entries, e => e.Id == "jimbos_orange_big" && e.DurationSeconds == 7200 && e.Magnitude == 50);
        Assert.Contains(r.Entries, e => e.Id == "jimbos_orange" && e.DurationSeconds == 600 && e.Magnitude == 50);

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
        var cash = Assert.Single(parsed.Rows, x => x.Id == "blank_check");
        Assert.Equal(EffectTarget.Cash, cash.Target);
        Assert.Equal(1, cash.Magnitude);
        var bulb = Assert.Single(parsed.Rows, x => x.Id == "quantum_bulb");
        Assert.Equal(EffectTarget.None, bulb.Target);
        var bulbMeta = Assert.IsAssignableFrom<IReadOnlyDictionary<string, System.Text.Json.JsonElement>>(bulb.Meta);
        Assert.Equal(600, bulbMeta["durationSeconds"].GetInt64());
    }
}
