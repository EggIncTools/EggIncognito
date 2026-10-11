using EggIncognito.Data.Services;
using EggIncognito.Models.Events;
using EggIncognito.Services.Predictions;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tests.Predictions;

public class EventPredictorTests {
    private const double Day = 86400d;

    private static List<EventRow> TemplateRows(string? stopped = null, DateOnly? stoppedFrom = null) =>
        EventTemplate.Build(EventTemplate.End.AddDays(-364), EventTemplate.End.AddDays(120), stopped, stoppedFrom);

    private static IReadOnlyList<EventPrediction> Predictions(int horizon) =>
        EventPredictor.Predict(TemplateRows(), EventTemplate.AsOf, horizon);

    private static DayOfWeek Weekday(EventPrediction prediction) =>
        NoonEastern.LocalDate(prediction.PredictedStart).DayOfWeek;

    [Theory]
    [InlineData("prestige-boost", DayOfWeek.Saturday)]
    [InlineData("piggy-boost", DayOfWeek.Saturday)]
    [InlineData("piggy-boost", DayOfWeek.Wednesday)]
    [InlineData("earnings-boost", DayOfWeek.Monday)]
    [InlineData("research-sale", DayOfWeek.Friday)]
    public void Predict_TemplateHistory_DerivesWeekdayLanes(string type, DayOfWeek weekday) {
        var lane = Predictions(56)
            .Where(p => p.Kind == EventRuleKind.WeekdayLane && p.Type == type && Weekday(p) == weekday)
            .ToList();

        Assert.NotEmpty(lane);
        Assert.All(lane, p => {
            Assert.Equal(7, p.PeriodDays);
            Assert.False(p.Ultra);
            Assert.True(p.Confidence >= 0.8);
            Assert.Equal(type, Assert.Single(p.Candidates).Type);
            Assert.Contains("every 7 days", p.Evidence);
        });
    }

    [Fact]
    public void Predict_TemplateHistory_EpicAndCraftingSundaysAlternate() {
        var sundays = Predictions(56)
            .Where(p => p.Kind == EventRuleKind.Alternating)
            .OrderBy(p => p.PredictedStart)
            .ToList();

        Assert.True(sundays.Count >= 4);
        Assert.All(sundays, p => Assert.Equal(DayOfWeek.Sunday, Weekday(p)));
        for (int i = 1; i < sundays.Count; i++) {
            Assert.NotEqual(sundays[i - 1].Type, sundays[i].Type);
            Assert.Equal(7, Days(sundays[i - 1], sundays[i]));
        }

        Assert.Equal(["crafting-sale", "epic-research-sale"], sundays.Select(p => p.Type).Distinct().Order().ToList());
    }

    [Fact]
    public void Predict_TemplateHistory_MissionCapacityIsPeriodicOnCraftingSundays() {
        var capacity = Predictions(84)
            .Where(p => p.Kind == EventRuleKind.Periodic && p.Type == "mission-capacity")
            .OrderBy(p => p.PredictedStart)
            .ToList();
        var crafting = Predictions(84)
            .Where(p => p.Type == "crafting-sale")
            .Select(p => NoonEastern.LocalDate(p.PredictedStart))
            .ToHashSet();

        Assert.True(capacity.Count >= 2);
        Assert.All(capacity, p => {
            Assert.Equal(28, p.PeriodDays);
            Assert.Equal(2 * Day, p.PredictedEnd - p.PredictedStart);
            Assert.Contains(NoonEastern.LocalDate(p.PredictedStart), crafting);
            Assert.Contains("on crafting-sale", p.Evidence);
        });
    }

    [Fact]
    public void Predict_TemplateHistory_PoolSlotsOnlyOnMidweekDays() {
        var pool = Predictions(28).Where(p => p.Kind == EventRuleKind.Pool).ToList();

        Assert.Equal(12, pool.Count);
        Assert.All(pool.GroupBy(p => NoonEastern.LocalDate(p.PredictedStart)), g => Assert.Single(g));
        Assert.Equal(
            [DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday],
            pool.Select(Weekday).Distinct().Order().ToList());
        Assert.All(pool, p => {
            Assert.Equal(1, p.PeriodDays);
            Assert.NotNull(p.Type);
            Assert.Equal(Day, p.PredictedEnd - p.PredictedStart);
        });
    }

    [Fact]
    public void Predict_TemplateHistory_PoolCandidatesAreAFullDistribution() {
        var pool = Predictions(28).First(p => p.Kind == EventRuleKind.Pool);

        Assert.True(pool.Candidates.Count >= 2);
        Assert.InRange(pool.Candidates.Sum(c => c.Probability), 0.9999999, 1.0000001);
        Assert.Equal(pool.Candidates[0].Type, pool.Type);
        Assert.Equal(pool.Candidates[0].Probability, pool.Confidence);
        Assert.Equal(pool.Candidates.OrderByDescending(c => c.Probability).Select(c => c.Type), pool.Candidates.Select(c => c.Type));
    }

    [Fact]
    public void Predict_TemplateHistory_SaturdayCarriesPrestigeAndFortyEightHourPiggy() {
        var standard = Predictions(28).Where(p => !p.Ultra && Weekday(p) == DayOfWeek.Saturday).ToList();
        var first = NoonEastern.LocalDate(standard.Min(p => p.PredictedStart));
        var day = standard.Where(p => NoonEastern.LocalDate(p.PredictedStart) == first).ToList();

        Assert.Equal(2, day.Count);
        Assert.Contains(day, p => p.Type == "prestige-boost" && p.PredictedEnd - p.PredictedStart == Day);
        Assert.Contains(day, p => p.Type == "piggy-boost" && p.PredictedEnd - p.PredictedStart == 2 * Day);
    }

    [Fact]
    public void Predict_TemplateHistory_UltraRunsEveryTwoDaysWithoutRepeatingLastActual() {
        var rows = TemplateRows();
        double asOf = EventTemplate.AsOf;
        var ultra = EventPredictor.Predict(rows, asOf, 28)
            .Where(p => p.Kind == EventRuleKind.Ultra)
            .OrderBy(p => p.PredictedStart)
            .ToList();
        var lastActual = rows.Where(r => r.Ultra && r.Start < asOf).OrderBy(r => r.Start).ToList()[^1];

        Assert.Equal(14, ultra.Count);
        Assert.All(ultra, p => {
            Assert.True(p.Ultra);
            Assert.Equal(2, p.PeriodDays);
            Assert.NotNull(p.Type);
        });
        for (int i = 1; i < ultra.Count; i++) Assert.Equal(2, Days(ultra[i - 1], ultra[i]));
        Assert.Equal(
            2,
            NoonEastern.LocalDate(ultra[0].PredictedStart).DayNumber
            - NoonEastern.LocalDate(lastActual.Start).DayNumber);
        Assert.NotEqual(lastActual.Type, ultra[0].Type);
    }

    [Fact]
    public void Predict_TemplateHistory_UltraSlotsRankEveryTypeAndNeverRepeatTheLastGuess() {
        var ultra = Predictions(28).Where(p => p.Kind == EventRuleKind.Ultra).OrderBy(p => p.PredictedStart).ToList();

        Assert.Equal(14, ultra.Count);
        Assert.All(ultra, p => {
            Assert.NotNull(p.Type);
            Assert.True(p.Candidates.Count >= 2);
            Assert.Equal(p.Type, p.Candidates[0].Type);
            Assert.InRange(p.Candidates.Sum(c => c.Probability), 0.99, 1.01);
        });
        for (int i = 1; i < ultra.Count; i++) Assert.NotEqual(ultra[i - 1].Type, ultra[i].Type);
    }

    [Fact]
    public void Predict_LaneStoppedTenWeeksAgo_DropsLaneAndScoresTypeLow() {
        var rows = TemplateRows("research-sale", EventTemplate.End.AddDays(-70));

        var predictions = EventPredictor.Predict(rows, EventTemplate.AsOf, 28);

        Assert.DoesNotContain(predictions, p => p.Type == "research-sale");
        Assert.DoesNotContain(predictions, p => !p.Ultra && Weekday(p) == DayOfWeek.Friday);
        Assert.All(
            predictions.SelectMany(p => p.Candidates).Where(c => c.Type == "research-sale"),
            c => Assert.True(c.Probability < 0.15));
    }

    [Fact]
    public void Predict_MixedPeriodicGaps_UsesTheLatestGap() {
        var anchor = EventTemplate.End.AddDays(-13);
        var rows = TemplateRows().Where(r => r.Type != "mission-capacity").ToList();
        foreach (int back in new[] { 0, 28, 56, 84, 126, 168 }) rows.Add(TwoDayRow("mission-capacity", anchor.AddDays(-back)));

        var lane = EventPredictor.Predict(rows, EventTemplate.AsOf, 56)
            .Where(p => p.Kind == EventRuleKind.Periodic && p.Type == "mission-capacity")
            .ToList();

        var first = Assert.Single(lane, p => NoonEastern.LocalDate(p.PredictedStart) == anchor.AddDays(28));
        Assert.Equal(28, first.PeriodDays);
        Assert.Contains("42 before", first.Evidence);
    }

    [Fact]
    public void Predict_FourWeekRunOnWeeklyGrid_DoesNotBecomeLane() {
        var monday = EventTemplate.End.AddDays(-5);
        var rows = TemplateRows();
        foreach (int back in new[] { 0, 7, 14, 21 }) rows.Add(TwoDayRow("hab-sale", monday.AddDays(-back)));

        var predictions = EventPredictor.Predict(rows, EventTemplate.AsOf, 28);

        Assert.DoesNotContain(
            predictions, p => p.Kind == EventRuleKind.WeekdayLane && p.Type == "hab-sale" && Weekday(p) == DayOfWeek.Monday);
    }

    private static EventRow TwoDayRow(string type, DateOnly day) {
        double start = NoonEastern.SlotTime(day);
        return new EventRow(type, false, start, start + 2 * Day);
    }

    [Fact]
    public void Predict_NoRowsInWindow_ReturnsNothing() {
        var rows = EventTemplate.Build(EventTemplate.End.AddDays(-500), EventTemplate.End.AddDays(-200));

        Assert.Empty(EventPredictor.Predict(rows, EventTemplate.AsOf, 28));
    }

    [Fact]
    public void Predict_HorizonOutOfRange_ClampsToOneAndNinetyDays() {
        var rows = TemplateRows();
        double asOf = EventTemplate.AsOf;

        var low = EventPredictor.Predict(rows, asOf, 0);
        var high = EventPredictor.Predict(rows, asOf, 500);

        Assert.NotEmpty(low);
        Assert.All(low, p => Assert.InRange(p.PredictedStart, asOf, asOf + Day));
        Assert.Equal(EventPredictor.Predict(rows, asOf, 90).Count, high.Count);
        Assert.All(high, p => Assert.InRange(p.PredictedStart, asOf, asOf + 90 * Day));
    }

    [Fact]
    public void Predict_TemplateHistory_NeverRepeatsAStandardTypeOnConsecutiveDays() {
        var byDate = Predictions(28)
            .Where(p => !p.Ultra)
            .GroupBy(p => NoonEastern.LocalDate(p.PredictedStart))
            .ToDictionary(g => g.Key, g => g.Select(p => p.Type).OfType<string>().ToHashSet(StringComparer.Ordinal));

        foreach (var (date, types) in byDate) {
            if (!byDate.TryGetValue(date.AddDays(1), out var next)) continue;
            Assert.DoesNotContain(next, types.Contains);
        }
    }

    [Fact]
    public void Train_TemplateHistory_ReportsEveryRuleKindWithEvidence() {
        var model = EventPredictor.Train(TemplateRows(), EventTemplate.AsOf);

        var kinds = model.Rules.Select(r => r.Kind).Distinct().Order().ToList();
        Assert.Equal([.. Enum.GetValues<EventRuleKind>().Order()], kinds);
        Assert.All(model.Rules, r => Assert.False(string.IsNullOrWhiteSpace(r.Evidence.Summary)));
        Assert.All(model.Rules, r => Assert.True(r.Evidence.Expected > 0));
    }

    [Fact]
    public async Task GetAsync_CacheVersionMatchesCurrent_PredictsFromCachedRowsWithoutQuerying() {
        var opts = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1").Options;
        var db = new EggIncognitoDbContext(opts);
        var version = new EventDataVersion();
        var cache = new EventPredictionCache();
        cache.Set(version.Version, TemplateRows());
        var predictor = new EventPredictor(db, version, cache, TimeProvider.System);

        var result = await predictor.GetAsync(28, EventTemplate.AsOf);

        Assert.Equal(EventTemplate.AsOf, result.GeneratedAt);
        Assert.NotEmpty(result.Predictions);
        Assert.All(result.Predictions, p => Assert.True(p.PredictedStart >= result.GeneratedAt));
    }

    [Fact]
    public void Watch_TemplateHistory_PairsLastRunWithUpcomingForecastsPerType() {
        var rows = TemplateRows();
        var predictions = Predictions(28);

        var watch = EventPredictor.Watch(rows, predictions, EventTemplate.AsOf);

        var earnings = Assert.Single(watch, e => e.Key == "earnings-boost:std:0");
        Assert.Equal("Earnings Boost", earnings.Label);
        Assert.False(earnings.Ultra);
        Assert.Equal(rows.Where(r => r.Type == "earnings-boost" && !r.Ultra && r.Start < EventTemplate.AsOf).Max(r => r.Start), earnings.LastStart);
        Assert.NotEmpty(earnings.Next);
        Assert.All(earnings.Next, f => Assert.True(f.Start >= EventTemplate.AsOf));
        Assert.Equal(earnings.Next.OrderBy(f => f.Start).Select(f => f.Start), earnings.Next.Select(f => f.Start));
        Assert.All(watch, e => Assert.Equal(e.Ultra, e.Key.Contains(":ultra:", StringComparison.Ordinal)));
        Assert.Contains(watch, e => e.Ultra && e.Next.Count > 0);
    }

    [Fact]
    public void Watch_SplitsATypeByMagnitudeSeenInTheLastYear_AndSharesForecastsByFrequency() {
        var rows = TemplateRows().Select(r => r.Type == "prestige-boost"
            ? r with { Multiplier = NoonEastern.LocalDate(r.Start).DayNumber / 7 % 4 == 0 ? 3 : 2 }
            : r).ToList();

        var watch = EventPredictor.Watch(rows, EventPredictor.Predict(rows, EventTemplate.AsOf, 28), EventTemplate.AsOf);

        var twoX = Assert.Single(watch, e => e.Key == "prestige-boost:std:2");
        var threeX = Assert.Single(watch, e => e.Key == "prestige-boost:std:3");
        Assert.Equal("Prestige Boost 2x", twoX.Label);
        Assert.Equal("Prestige Boost 3x", threeX.Label);
        var prestige = rows.Where(r => r.Type == "prestige-boost" && r.Start < EventTemplate.AsOf).ToList();
        Assert.Equal(prestige.Where(r => r.Multiplier == 2).Max(r => r.Start), twoX.LastStart);
        Assert.Equal(prestige.Where(r => r.Multiplier == 3).Max(r => r.Start), threeX.LastStart);
        Assert.Equal(twoX.Next.Select(f => f.Start), threeX.Next.Select(f => f.Start));
        Assert.All(twoX.Next.Zip(threeX.Next), pair => {
            Assert.True(pair.First.Confidence > pair.Second.Confidence);
            Assert.InRange(pair.First.Confidence + pair.Second.Confidence, 0, 1.000001);
        });
        Assert.DoesNotContain(watch, e => e.Key.StartsWith("prestige-boost:std:0", StringComparison.Ordinal));
    }

    private static int Days(EventPrediction a, EventPrediction b) =>
        NoonEastern.LocalDate(b.PredictedStart).DayNumber - NoonEastern.LocalDate(a.PredictedStart).DayNumber;
}
