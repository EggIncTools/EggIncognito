using EggIncognito.Services.Coverage;

namespace EggIncognito.Tests.Coverage;

public sealed class ConsumptionReportRowsMaterializeTests {
    private static RawConsumptionEvent Ev(long id, string family, string level, string rarity, string byproducts, double gold = 0, double piggy = 0, string? version = null) =>
        new(id, family, level, rarity, byproducts, gold, piggy, version);

    [Fact]
    public void NonDroppingEventGetsZeroCountRowForCellCandidates() {
        var events = new[] {
            Ev(1, "GUSSET", "COMMON", "LEGENDARY", """[{"name":"LUNAR_STONE_FRAGMENT","level":"INFERIOR","rarity":"COMMON","count":1}]"""),
            Ev(2, "GUSSET", "COMMON", "LEGENDARY", "[]"),
        };

        var rows = ConsumptionReportRows.Materialize(events);

        var missed = Assert.Single(rows, r => r.EventId == "2");
        Assert.Equal("LUNAR_STONE_FRAGMENT", missed.OutputItem);
        Assert.Equal(0, missed.Count);
    }

    [Fact]
    public void RowsCarryTheirOwnEventIdAsSampleKey() {
        var events = new[] {
            Ev(7, "GUSSET", "COMMON", "LEGENDARY",
                """[{"name":"LUNAR_STONE_FRAGMENT","level":"INFERIOR","rarity":"COMMON","count":2}]""", gold: 5, piggy: 8, version: "1.2.3"),
        };

        var rows = ConsumptionReportRows.Materialize(events);

        var row = Assert.Single(rows);
        Assert.Equal("7", row.EventId);
        Assert.Equal(2, row.Count);
        Assert.Equal(5, row.GoldenEggs);
        Assert.Equal(8, row.PiggyFill);
        Assert.Equal("1.2.3", row.ClientVersion);
    }

    [Fact]
    public void CandidatesAreScopedPerInputCell() {
        var events = new[] {
            Ev(1, "GUSSET", "COMMON", "LEGENDARY", """[{"name":"LUNAR_STONE_FRAGMENT","level":"INFERIOR","rarity":"COMMON","count":1}]"""),
            Ev(2, "FEATHER", "COMMON", "LEGENDARY", "[]"),
        };

        var rows = ConsumptionReportRows.Materialize(events);

        Assert.DoesNotContain(rows, r => r.EventId == "2" && r.OutputItem == "LUNAR_STONE_FRAGMENT");
    }
}
