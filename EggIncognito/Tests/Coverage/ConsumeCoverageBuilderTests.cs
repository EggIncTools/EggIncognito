using EggIncognito.Models.Coverage;
using EggIncognito.Services.Coverage;

namespace EggIncognito.Tests.Coverage;

public class ConsumeCoverageBuilderTests {
    private static readonly CoverageTargetRow Global = new(1, null, null, null, 200, 10, true);

    private static readonly IReadOnlyList<CoverageCatalogCell> Catalog = [
        new("TUNGSTEN_ANKH", "INFERIOR", 0, "COMMON", 0),
        new("TUNGSTEN_ANKH", "LESSER", 1, "COMMON", 0),
        new("LUNAR_TOTEM", "INFERIOR", 0, "COMMON", 0),
        new("SOUL_STONE", "INFERIOR", 0, "COMMON", 0)
    ];

    private static readonly IReadOnlyDictionary<string, CoverageFamilyInfo> Families =
        new Dictionary<string, CoverageFamilyInfo>(StringComparer.Ordinal) {
            ["TUNGSTEN_ANKH"] = new("TUNGSTEN_ANKH", "TUNGSTEN ANKHS", 11, ["CRUDE TUNGSTEN ANKH", "TUNGSTEN ANKH"]),
            ["LUNAR_TOTEM"] = new("LUNAR_TOTEM", "LUNAR TOTEMS", 0, ["BASIC LUNAR TOTEM"])
        };

    private static CoverageSample Ankh(int quantity) =>
        new("TUNGSTEN_ANKH", "INFERIOR", "COMMON", quantity);

    private static CoverageCell Cell(ConsumeCoverageMap map, string spec, string level) =>
        map.Families.Single(f => f.SpecName == spec).Tiers.Single(t => t.Level == level).Cells.Single();

    private static ConsumeCoverageMap Build(IEnumerable<CoverageSample> samples, params CoverageTargetRow[] targets) =>
        ConsumeCoverageBuilder.Build(Catalog, Families, samples, targets.Length == 0 ? [Global] : targets);

    [Fact]
    public void BulkConsume_CountsQuantityAsItemsAndOneObservation() {
        var cell = Cell(Build([Ankh(29)]), "TUNGSTEN_ANKH", "INFERIOR");
        Assert.Equal(29, cell.Items);
        Assert.Equal(1, cell.Observations);
        Assert.Equal(0, cell.Validity);
    }

    [Fact]
    public void TenBatchesOfTwenty_CompleteTheCell() {
        var map = Build(Enumerable.Range(0, 10).Select(_ => Ankh(20)));
        var cell = Cell(map, "TUNGSTEN_ANKH", "INFERIOR");
        Assert.Equal(1, cell.Validity);
        Assert.Equal(1, map.CellsComplete);
    }

    [Fact]
    public void SingleLargeConsume_NeverCompletesAMultiObservationTarget() =>
        Assert.Equal(0, Cell(Build([Ankh(500)]), "TUNGSTEN_ANKH", "INFERIOR").Validity);

    [Theory]
    [InlineData(100, 10, 200, 10, 0.5)]
    [InlineData(400, 4, 200, 10, 3 / 9.0)]
    [InlineData(5, 1, 5, 1, 1)]
    public void Validity_IsTheWeakerOfItemsAndObservations(int items, int obs, int itemTarget, int obsTarget,
        double expected) =>
        Assert.Equal(expected, ConsumeCoverageBuilder.Validity(items, obs, itemTarget, obsTarget), 9);

    [Fact]
    public void MostSpecificTarget_Wins() {
        var map = Build([],
            Global,
            new CoverageTargetRow(2, "TUNGSTEN_ANKH", null, null, 50, 5, true),
            new CoverageTargetRow(3, "TUNGSTEN_ANKH", "INFERIOR", "COMMON", 20, 2, true));
        var cell = Cell(map, "TUNGSTEN_ANKH", "INFERIOR");
        var sibling = Cell(map, "TUNGSTEN_ANKH", "LESSER");
        var other = Cell(map, "LUNAR_TOTEM", "INFERIOR");
        Assert.Equal((20, 2, 3L, (long?)3), (cell.ItemTarget, cell.ObservationTarget, cell.ResolvedTargetId, cell.CellTargetId));
        Assert.Equal((50, 5, 2L, (long?)null), (sibling.ItemTarget, sibling.ObservationTarget, sibling.ResolvedTargetId, sibling.CellTargetId));
        Assert.Equal((200, 10, 1L), (other.ItemTarget, other.ObservationTarget, other.ResolvedTargetId));
    }

    [Fact]
    public void NoTargetRows_FallBackToDefaults() {
        var cell = Cell(ConsumeCoverageBuilder.Build(Catalog, Families, [], []), "LUNAR_TOTEM", "INFERIOR");
        Assert.Equal((ConsumeCoverageBuilder.DefaultItemTarget, ConsumeCoverageBuilder.DefaultObservationTarget, 0L),
            (cell.ItemTarget, cell.ObservationTarget, cell.ResolvedTargetId));
        Assert.True(cell.InScope);
    }

    [Fact]
    public void DisabledFamily_LeavesTheRollup() {
        var map = Build(Enumerable.Range(0, 10).Select(_ => Ankh(20)),
            Global,
            new CoverageTargetRow(2, "LUNAR_TOTEM", null, null, 200, 10, false));
        Assert.False(Cell(map, "LUNAR_TOTEM", "INFERIOR").InScope);
        Assert.Equal(2, map.CellsInScope);
        Assert.Equal(0.5, map.Validity, 9);
        var lunar = map.Families.Single(f => f.SpecName == "LUNAR_TOTEM");
        Assert.Equal(0, lunar.CellsInScope);
        Assert.Equal(0, lunar.Validity);
    }

    [Fact]
    public void SampleOutsideTheCatalog_IsIgnored() {
        var map = Build([new CoverageSample("PUZZLE_CUBE", "INFERIOR", "COMMON", 40)]);
        Assert.Equal(0, map.Items);
        Assert.Equal(0, map.Observations);
    }

    [Fact]
    public void CatalogFamilyWithoutInfo_IsNotEmitted() {
        var map = Build([new CoverageSample("SOUL_STONE", "INFERIOR", "COMMON", 40)]);
        Assert.DoesNotContain(map.Families, f => f.SpecName == "SOUL_STONE");
        Assert.Equal(3, map.CellsInScope);
        Assert.Equal(0, map.Items);
    }

    [Fact]
    public void Families_AreOrderedAndTiersNamed() {
        var map = Build([]);
        Assert.Equal(["LUNAR_TOTEM", "TUNGSTEN_ANKH"], map.Families.Select(f => f.SpecName));
        Assert.Equal(["CRUDE TUNGSTEN ANKH", "TUNGSTEN ANKH"], map.Families[1].Tiers.Select(t => t.Name));
    }

    [Fact]
    public void SuggestedBatch_SpreadsShortfallOverRemainingObservations() {
        var cell = Cell(Build([Ankh(20)]), "TUNGSTEN_ANKH", "INFERIOR");
        Assert.Equal(180, cell.ItemsShort);
        Assert.Equal(9, cell.ObservationsShort);
        Assert.Equal(20, cell.SuggestedBatch);
    }
}
