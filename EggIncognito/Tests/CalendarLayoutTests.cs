using EggIncognito.Models.Calendar;
using EggIncognito.Services.Calendar;
using EggIncognito.Services.Events;

namespace EggIncognito.Tests;

public class CalendarLayoutTests {
    private sealed record Span(string Key, double Start, double End) : ICalendarSpan;

    private static Span Item(string key, DateTimeOffset start, DateTimeOffset end) =>
        new(key, UnixSeconds.FromTime(start), UnixSeconds.FromTime(end));

    [Fact]
    public void Rows_EventStartingAsAnotherEnds_SharesItsLane() {
        var zone = TimeZoneInfo.Utc;
        var (start, end) = CalendarLayout.Window(
            new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero), CalendarZoom.Week, zone);
        var items = new[] {
            Item("research", start.AddDays(1), start.AddDays(2)),
            Item("piggy", start.AddDays(1.5), start.AddDays(3)),
            Item("prestige", start.AddDays(2), start.AddDays(3)),
        };

        var row = Assert.Single(CalendarLayout.Rows(items, start, end, CalendarZoom.Week, start, zone));

        Assert.Equal(2, row.Lanes.Count);
        Assert.Equal(["research", "prestige"], row.Lanes[0].Select(b => b.Item.Key));
    }

    [Fact]
    public void Rows_MonthZoom_OneRowPerWeekWithOutOfMonthDaysMuted() {
        var zone = TimeZoneInfo.Utc;
        var (start, end) = CalendarLayout.Window(
            new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero), CalendarZoom.Month, zone);

        var rows = CalendarLayout.Rows(Array.Empty<Span>(), start, end, CalendarZoom.Month, start, zone);

        Assert.Equal(5, rows.Count);
        Assert.True(rows[0].Cells[0].Muted);
        Assert.False(rows[0].Cells[^1].Muted);
    }
}

public class DateLabelsTests {
    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(23, "23rd")]
    [InlineData(31, "31st")]
    public void Ordinal_UsesEnglishSuffixes(int day, string expected) =>
        Assert.Equal(expected, DateLabels.Ordinal(day));

    [Fact]
    public void MonthDay_IsAbbreviatedMonthPlusOrdinal() =>
        Assert.Equal("Sep 3rd", DateLabels.MonthDay(new DateTime(2026, 9, 3)));
}
