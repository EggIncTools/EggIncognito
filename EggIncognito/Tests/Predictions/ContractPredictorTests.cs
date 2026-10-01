using EggIncognito.Data.Services;
using EggIncognito.Models.Contracts;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions;
using EggIncognito.Services.Predictions.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace EggIncognito.Tests.Predictions;

public class ContractPredictorTests {
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private const double Week = 7 * 86400d;

    private static DateTimeOffset Local(double unixSeconds) =>
        TimeZoneInfo.ConvertTime(UnixSeconds.ToTime(unixSeconds), Zone);

    private static EggIncognitoDbContext UnreachableDb() =>
        new(new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1").Options);

    private static (ContractPredictor Predictor, ContractDataVersion Version) Primed(
        IReadOnlyList<ContractReleaseSample> samples) {
        var version = new ContractDataVersion();
        var cache = new ContractPredictionCache();
        cache.Set(version.Version, samples);
        var predictor = new ContractPredictor(
            UnreachableDb(), version, cache, NullLogger<ContractPredictor>.Instance, TimeProvider.System);
        return (predictor, version);
    }

    private static double AsOf => UnixSeconds.FromTime(ContractTemplate.AsOf);

    [Fact]
    public void Grid_IsTheStandingWeekdayRuleWithConformanceEvidence() {
        var model = ContractModel.Train(ContractTemplate.Build(26), AsOf);

        var grid = model.Grid.Slots.ToDictionary(s => s.Kind);
        Assert.Equal(DayOfWeek.Monday, grid[ContractSlotKind.NewContract].Weekday);
        Assert.Equal(DayOfWeek.Wednesday, grid[ContractSlotKind.Leggacy].Weekday);
        Assert.Equal(DayOfWeek.Friday, grid[ContractSlotKind.PeLeggacy].Weekday);
        Assert.Equal(DayOfWeek.Friday, grid[ContractSlotKind.PeLeggacyUltra].Weekday);
        Assert.All(grid.Values, s => Assert.Equal(new TimeOnly(12, 0), s.Time));
        Assert.All(grid.Values, s => Assert.Equal(s.Evidence.Expected, s.Evidence.Observed));
        Assert.All(grid.Values, s => Assert.True(s.Evidence.Expected > 0));
        Assert.All(grid.Values, s => Assert.Contains("standing rule", s.Evidence.Summary));
    }

    [Fact]
    public void Grid_NoRecentReleasesOfAKind_KeepsTheSlotWithEmptyEvidence() {
        var samples = ContractTemplate.Build(26).Where(s => s.ReleaseKind != ContractSlotKind.Leggacy).ToList();

        var model = ContractModel.Train(samples, AsOf);

        Assert.Equal(4, model.Grid.Slots.Count);
        Assert.Equal(0, model.Grid.SlotFor(ContractSlotKind.Leggacy).Evidence.Expected);
    }

    [Fact]
    public void Grid_OffGridRelease_CountsAgainstConformance() {
        var samples = ContractTemplate.Build(26);
        var moved = samples.First(s => s.ReleaseKind == ContractSlotKind.Leggacy);
        samples[samples.IndexOf(moved)] = moved with { Start = moved.Start + 86400d };

        var model = ContractModel.Train(samples, AsOf);

        var slot = model.Grid.SlotFor(ContractSlotKind.Leggacy);
        Assert.Equal(slot.Evidence.Expected - 1, slot.Evidence.Observed);
        Assert.False(ReleaseGrid.IsGridSlot(moved.Start + 86400d));
        Assert.True(ReleaseGrid.IsGridSlot(moved.Start));
    }

    [Fact]
    public void Slots_FromThursday_StartWithTheFridayPairThenMonday() {
        var model = ContractModel.Train(ContractTemplate.Build(26), AsOf);
        var thursday = ContractTemplate.Day(ContractTemplate.LastMonday.AddDays(3)).AddHours(2);

        var slots = model.Slots(thursday, 4, UnixSeconds.FromTime(thursday));

        Assert.Equal(
            [ContractSlotKind.PeLeggacy, ContractSlotKind.PeLeggacyUltra, ContractSlotKind.NewContract, ContractSlotKind.Leggacy],
            slots.Select(s => s.Kind).ToList());
        Assert.Equal(slots[0].SlotTime, slots[1].SlotTime);
        Assert.Equal(DayOfWeek.Friday, Local(slots[0].SlotTime).DayOfWeek);
        Assert.Equal(12, Local(slots[0].SlotTime).Hour);
    }

    [Fact]
    public void Pools_PeContractAlternatesStandardAndUltraOnEveryRelease() {
        var samples = ContractTemplate.Build(26);
        var pools = OldestFirstPools.Fit(samples);

        var standard = pools[ContractSlotKind.PeLeggacy].Candidates.Select(c => c.ContractId).ToHashSet();
        var ultra = pools[ContractSlotKind.PeLeggacyUltra].Candidates.Select(c => c.ContractId).ToHashSet();
        var lastKind = samples.Where(s => s.ProphecyEggs > 0).GroupBy(s => s.ContractId)
            .ToDictionary(g => g.Key, g => g.MaxBy(s => s.Start).UltraOnly);

        Assert.NotEmpty(standard);
        Assert.NotEmpty(ultra);
        Assert.Empty(standard.Intersect(ultra));
        Assert.All(standard, id => Assert.True(lastKind[id]));
        Assert.All(ultra, id => Assert.False(lastKind[id]));
    }

    [Fact]
    public void Pools_CandidatesAreOldestLastReleaseFirst() {
        var pools = OldestFirstPools.Fit(ContractTemplate.Build(26));

        foreach (var kind in new[] { ContractSlotKind.Leggacy, ContractSlotKind.PeLeggacy, ContractSlotKind.PeLeggacyUltra }) {
            var last = pools[kind].Candidates.Select(c => c.LastReleased).ToList();
            Assert.Equal([.. last.Order()], last);
        }

        Assert.Empty(pools[ContractSlotKind.NewContract].Candidates);
    }

    [Fact]
    public void Pools_NewContractsAreLeggacyCandidatesFromTheirFirstRelease() {
        var pools = OldestFirstPools.Fit(ContractTemplate.Build(26));

        var leggacy = pools[ContractSlotKind.Leggacy];
        Assert.Equal(1, leggacy.Candidates.Single(c => c.ContractId == "new-25").Releases);
        Assert.True(leggacy.Candidates.Single(c => c.ContractId == "seed-0").Releases >= 2);
        Assert.NotNull(leggacy.GapSeconds);
        Assert.InRange(leggacy.GapSeconds.Value / (7 * 86400d), 10, 20);
    }

    [Fact]
    public void Pools_LengthIsTheMedianReleaseLengthOfTheKind() {
        var pools = OldestFirstPools.Fit(ContractTemplate.Build(26));

        Assert.Equal(ContractTemplate.LeggacyLengthDays * 86400d, pools[ContractSlotKind.Leggacy].LengthSeconds);
        Assert.Equal(ContractTemplate.PeLengthDays * 86400d, pools[ContractSlotKind.PeLeggacy].LengthSeconds);
    }

    [Fact]
    public void Top_GatedGap_ExcludesCandidatesOlderThanTwiceThePoolGap() {
        double now = UnixSeconds.FromTime(ContractTemplate.AsOf);
        ContractReleaseSample[] samples = [
            Leggacy("gapper", now - 8 * Week), Leggacy("gapper", now - 6 * Week), Leggacy("gapper", now - 4 * Week),
            Leggacy("gapper", now - 2 * Week),
            Leggacy("gapper2", now - 5 * Week), Leggacy("gapper2", now - 3 * Week),
            Leggacy("stale", now - 30 * Week),
            Leggacy("fresh", now - 1 * Week)
        ];

        var top = OldestFirstPools.Top(OldestFirstPools.Fit(samples)[ContractSlotKind.Leggacy], now);

        Assert.Equal(["gapper2", "gapper", "fresh"], top.Select(c => c.ContractId).ToList());
    }

    [Fact]
    public void Top_NoGatedGap_UsesThreeYearCutoff() {
        double now = UnixSeconds.FromTime(ContractTemplate.AsOf);
        ContractReleaseSample[] samples = [Leggacy("ancient", now - 209 * Week), Leggacy("recent", now - 52 * Week)];

        var top = OldestFirstPools.Top(OldestFirstPools.Fit(samples)[ContractSlotKind.Leggacy], now);

        Assert.Equal("recent", Assert.Single(top).ContractId);
    }

    [Fact]
    public void Backtest_TemplateHistory_HitsEverySlotAndKeepsTheActualInTheTopFive() {
        var samples = ContractTemplate.Build(3 * ContractTemplate.PePoolSize);
        double asOf = UnixSeconds.FromTime(ContractTemplate.Day(ContractTemplate.LastMonday.AddDays(-28)).AddHours(-1));

        var result = ContractBacktest.Run(samples, asOf, 9);

        Assert.Equal(0, result.ActualUncovered);
        Assert.All(result.Kinds, k => Assert.Equal(k.Predicted, k.SlotHit));
        foreach (var kind in new[] { ContractSlotKind.Leggacy, ContractSlotKind.PeLeggacy, ContractSlotKind.PeLeggacyUltra }) {
            var row = result.Kinds.Single(k => k.Kind == kind);
            Assert.True(row.Predicted > 0);
            Assert.True(row.Predicted == row.Top5Hit, $"{kind} top-5 {row.Top5Hit} of {row.Predicted}");
        }

        var ultra = result.Kinds.Single(k => k.Kind == ContractSlotKind.PeLeggacyUltra);
        Assert.True(ultra.Predicted == ultra.Top1Hit, $"ultra top-1 {ultra.Top1Hit} of {ultra.Predicted}");
    }

    [Fact]
    public async Task GetContractAsync_UnknownId_ReturnsNull() {
        var (predictor, _) = Primed(ContractTemplate.Build(26));
        Assert.Null(await predictor.GetContractAsync("missing"));
    }

    [Fact]
    public async Task GetContractAsync_KnownPeContract_EstimateSnapsToFridayNoon() {
        var samples = ContractTemplate.Build(26);
        var (predictor, _) = Primed(samples);
        var pe = samples.Where(s => s.ProphecyEggs > 0).GroupBy(s => s.ContractId).First(g => g.Count() >= 2);

        var estimate = await predictor.GetContractAsync(pe.Key);

        Assert.NotNull(estimate);
        Assert.NotNull(estimate.EstimatedNext);
        Assert.True(estimate.EstimatedNext >= UnixSeconds.FromTime(DateTimeOffset.UtcNow));
        Assert.Equal(DayOfWeek.Friday, Local(estimate.EstimatedNext.Value).DayOfWeek);
        Assert.Equal(12, Local(estimate.EstimatedNext.Value).Hour);
    }

    [Fact]
    public async Task GetSlotsAsync_CacheCurrent_ReusesCachedSamplesWithoutDatabase() {
        var (predictor, _) = Primed(ContractTemplate.Build(26, DateTimeOffset.UtcNow));

        var response = await predictor.GetSlotsAsync(3);

        Assert.InRange(response.Slots.Count, 3, 4);
        Assert.All(response.Slots, s => Assert.True(s.Candidates.Count <= 5));
        Assert.All(response.Slots, s => Assert.True(s.LengthSeconds > 0));
        Assert.All(response.Slots.Where(s => s.Kind == ContractSlotKind.NewContract), s => Assert.Empty(s.Candidates));
    }

    [Fact]
    public async Task GetSlotsAsync_AfterVersionBump_RecomputesAndReachesDatabase() {
        var (predictor, version) = Primed(ContractTemplate.Build(26));
        version.Bump();

        var thrown = await Record.ExceptionAsync(() => predictor.GetSlotsAsync(3));

        Assert.NotNull(thrown);
        Assert.True(thrown is NpgsqlException or InvalidOperationException, thrown.ToString());
    }

    private static ContractReleaseSample Leggacy(string id, double start) =>
        new(id, id, start, 5 * 86400d, true, 0, false);
}
