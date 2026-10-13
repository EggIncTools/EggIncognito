using System.Text.Json.Nodes;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class GameDataFixTests {
    private const string Android = "egginc-android-1.37.so";

    [Theory]
    [InlineData("egginc")]
    [InlineData(Android)]
    public void Research_EveryPricedRowCarriesOnePricePerLevel(string capture) {
        if (!CaptureFixture.TryLoad(capture, out byte[] bin)) return;

        var catalog = CaptureFixture.ResearchCatalog(capture, bin);
        var prices = CaptureFixture.ResearchPrices(capture, bin);
        var curve = ResearchPriceCurveExtractor.Extract(bin);
        Assert.True(prices.Ok && curve.Ok, prices.Diagnostics + curve.Diagnostics);

        var doc = GameDataDocBuilders.BuildResearch(catalog.Entries, "test", prices.Prices, curve.Constants);
        EggSharp.GameData.GameDataProvider.Validate("research", doc.Json);
        var rows = JsonNode.Parse(doc.Json)!["rows"]!.AsArray().OfType<JsonObject>().ToList();
        foreach (var row in rows) {
            var meta = row["meta"]!.AsObject();
            if (meta["priceStart"] is null && meta["epicPriceStart"] is null) continue;
            var levels = meta["prices"]?.AsArray();
            Assert.True(levels is not null, $"{row["id"]} has no prices");
            Assert.Equal(row["maxLevel"]!.GetValue<int>(), levels!.Count);
        }

        double[] nests = Prices(rows, "comfy_nests");
        double[] known = [0.5, 3.26, 6.7, 11.38, 17.2, 25.02, 34.84, 47.8, 64.28, 85.66, 113.14, 148.26, 193.86, 250.0, 326.0];
        for (int i = 0; i < known.Length; i++) Assert.Equal(known[i], nests[i], 0.01 * known[i]);
        Assert.Equal(20, Prices(rows, "hold_to_research").Length);
        Assert.Equal(60, Prices(rows, "afx_mission_time").Length);
    }

    private static double[] Prices(List<JsonObject> rows, string id) =>
        [.. rows.First(r => r["id"]!.GetValue<string>() == id)["meta"]!["prices"]!.AsArray().Select(n => n!.GetValue<double>())];

    [Fact]
    public void Artifacts_ProphecyFamiliesUseTemplatePercentAsAdditiveDelta() {
        if (!CaptureFixture.TryLoad(Android, out byte[] bin)) return;

        var table = ArtifactTableExtractor.Extract(bin);
        Assert.True(table.Ok, table.Diagnostics);
        string artifactsJson = GameDataDocBuilders.BuildArtifacts(table.Families, "test").Json;
        EggSharp.GameData.GameDataProvider.Validate("artifacts", artifactsJson);
        var rows = JsonNode.Parse(artifactsJson)!["rows"]!.AsArray()
            .OfType<JsonObject>().ToDictionary(r => r["id"]!.GetValue<string>());

        double[] book = [.. new[] { "t0_r0", "t1_r0", "t2_r0", "t2_r2", "t3_r0", "t3_r2", "t3_r3" }
            .Select(k => rows[$"book_of_basan_{k}"]["magnitude"]!.GetValue<double>())];
        Assert.Equal([0.0025, 0.005, 0.0075, 0.008, 0.01, 0.011, 0.012], book, (a, b) => Math.Abs(a - b) < 1e-12);
        double[] stone = [.. new[] { "t0_r0", "t1_r0", "t2_r0" }
            .Select(k => rows[$"prophecy_stone_{k}"]["magnitude"]!.GetValue<double>())];
        Assert.Equal([0.0005, 0.001, 0.0015], stone, (a, b) => Math.Abs(a - b) < 1e-12);
        Assert.All(rows.Values.Where(r => r["id"]!.GetValue<string>().StartsWith("book_of_basan", StringComparison.Ordinal)
                                          || r["id"]!.GetValue<string>().StartsWith("prophecy_stone", StringComparison.Ordinal)),
            r => Assert.Equal("Add", r["combineMode"]!.GetValue<string>()));
    }

    [Fact]
    public void ArtifactDefinitions_FamilyLabelsComeFromTheEffectTemplates() {
        if (!CaptureFixture.TryLoad(Android, out byte[] bin)) return;

        var table = ArtifactTableExtractor.Extract(bin);
        var labels = DimensionLabelExtractor.Extract(bin);
        string definitions = ArtifactDefinitionsBuilder.Build(table, labels, "test");
        var parsed = EggSharp.GameData.Artifacts.ArtifactDefinitions.Parse(definitions);
        Assert.Equal("to Egg of Prophecy bonus", parsed.Families.Single(f => f.BinaryId == "book_of_basan").Label);
        var json = JsonNode.Parse(definitions)!;
        var byId = json["families"]!.AsArray().OfType<JsonObject>().ToDictionary(f => f["binaryId"]!.GetValue<string>());

        Assert.Equal("to Egg of Prophecy bonus", byId["book_of_basan"]["label"]?.GetValue<string>());
        Assert.Equal("to Egg of Prophecy bonus", byId["prophecy_stone"]["label"]?.GetValue<string>());
        Assert.Equal("enlightenment egg value", byId["light_of_eggendil"]["label"]?.GetValue<string>());
        Assert.Equal("effect of host artifact on enlightenment egg farms", byId["clarity_stone"]["label"]?.GetValue<string>());
        Assert.Null(byId["lunar_totem"]["label"]);
        Assert.Equal(0.0025, byId["book_of_basan"]["tiers"]![0]!["effects"]![0]!["magnitude"]!.GetValue<double>(), 12);
    }
}
