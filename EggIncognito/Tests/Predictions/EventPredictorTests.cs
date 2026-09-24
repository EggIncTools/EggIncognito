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
    [InlineData("prestige-boost", DayOfWeek.Saturday, 7)]
    [InlineData("piggy-boost", DayOfWeek.Saturday, 7)]
    [InlineData("piggy-boost", DayOfWeek.Wednesday, 7)]
    [InlineData("earnings-boost", DayOfWeek.Monday, 7)]
    [InlineData("research-sale", DayOfWeek.Friday, 7)]
    [InlineData("epic-research-sale", DayOfWeek.Sunday, 14)]
    [InlineData("crafting-sale", DayOfWeek.Sunday, 14)]
    [InlineData("mission-capacity", DayOfWeek.Sunday, 28)]
    public void Predict_TemplateHistory_DerivesFixedLanePeriod(string type, DayOfWeek weekday, int period) {
        var lane = Predictions(56)
            .Where(p => p.Kind == EventPredictionKind.Fixed && p.Type == type && Weekday(p) == weekday)
            .ToList();

        Assert.NotEmpty(lane);
        Assert.All(lane, p => {
            Assert.Equal(period, p.PeriodDays);
            Assert.False(p.Ultra);
            Assert.True(p.Confidence >= 0.8);
            Assert.Equal(type, Assert.Single(p.Candidates).Type);
        });
    }

    [Fact]
    public void Predict_TemplateHistory_EpicAndCraftingSundaysAlternate() {
        var sundays = Predictions(56)
            .Where(p => p.Kind == EventPredictionKind.Fixed && p.Type is "epic-research-sale" or "crafting-sale")
            .OrderBy(p => p.PredictedStart)
            .ToList();

        Assert.True(sundays.Count >= 4);
        for (int i = 1; i < sundays.Count; i++) {
            Assert.NotEqual(sundays[i - 1].Type, sundays[i].Type);
            Assert.Equal(7, Days(sundays[i - 1], sundays[i]));
        }
    }

    [Fact]
    public void Predict_TemplateHistory_PoolSlotsOnlyOnMidweekDays() {
        var pool = Predictions(28).Where(p => p.Kind == EventPredictionKind.Pool).ToList();

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
    public void Predict_TemplateHistory_PoolCandidatesAreNormalisedAndCapped() {
        var pool = Predictions(28).First(p => p.Kind == EventPredictionKind.Pool);

        Assert.InRange(pool.Candidates.Count, 1, 5);
        Assert.True(pool.Candidates.Sum(c => c.Probability) <= 1.0000001);
        Assert.Equal(pool.Candidates[0].Type, pool.Type);
        Assert.Equal(pool.Candidates[0].Probability, pool.Confidence);
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
            .Where(p => p.Kind == EventPredictionKind.Ultra)
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
    public void Predict_LaneCadenceTightenedMidWindow_QualifiesOnTrailingRun() {
        var anchor = EventTemplate.End.AddDays(-13);
        var rows = TemplateRows().Where(r => r.Type != "mission-capacity").ToList();
        foreach (int back in new[] { 0, 28, 56, 84, 126, 168 }) rows.Add(TwoDayRow("mission-capacity", anchor.AddDays(-back)));

        var lane = EventPredictor.Predict(rows, EventTemplate.AsOf, 56)
            .Where(p => p.Kind == EventPredictionKind.Fixed && p.Type == "mission-capacity")
            .ToList();

        var first = Assert.Single(lane, p => NoonEastern.LocalDate(p.PredictedStart) == anchor.AddDays(28));
        Assert.Equal(28, first.PeriodDays);
        Assert.InRange(first.Confidence, 0.7, 0.8);
    }

    [Fact]
    public void Predict_FourWeekRunOnWeeklyGrid_DoesNotBecomeLane() {
        var monday = EventTemplate.End.AddDays(-5);
        var rows = TemplateRows();
        foreach (int back in new[] { 0, 7, 14, 21 }) rows.Add(TwoDayRow("hab-sale", monday.AddDays(-back)));

        var predictions = EventPredictor.Predict(rows, EventTemplate.AsOf, 28);

        Assert.DoesNotContain(
            predictions, p => p.Kind == EventPredictionKind.Fixed && p.Type == "hab-sale" && Weekday(p) == DayOfWeek.Monday);
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
    public async Task GetAsync_CacheVersionMatchesCurrent_PredictsFromCachedRowsWithoutQuerying() {
        var opts = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1").Options;
        var db = new EggIncognitoDbContext(opts);
        var version = new EventDataVersion();
        var cache = new EventPredictionCache { Version = version.Version, Value = TemplateRows() };
        var predictor = new EventPredictor(db, version, cache, TimeProvider.System);

        var result = await predictor.GetAsync(28, EventTemplate.AsOf);

        Assert.Equal(EventTemplate.AsOf, result.GeneratedAt);
        Assert.NotEmpty(result.Predictions);
        Assert.All(result.Predictions, p => Assert.True(p.PredictedStart >= result.GeneratedAt));
    }

    private static int Days(EventPrediction a, EventPrediction b) =>
        NoonEastern.LocalDate(b.PredictedStart).DayNumber - NoonEastern.LocalDate(a.PredictedStart).DayNumber;
}
