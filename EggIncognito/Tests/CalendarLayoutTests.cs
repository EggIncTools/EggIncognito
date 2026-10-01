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
        var (start, end) = CalendarLayout.Window(
            new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero), CalendarZoom.Week);
        var items = new[] {
            Item("research", start.AddDays(1), start.AddDays(2)),
            Item("piggy", start.AddDays(1.5), start.AddDays(3)),
            Item("prestige", start.AddDays(2), start.AddDays(3)),
        };

        var row = Assert.Single(CalendarLayout.Rows(items, start, end, CalendarZoom.Week, start));

        Assert.Equal(2, row.Lanes.Count);
        Assert.Equal(["research", "prestige"], row.Lanes[0].Select(b => b.Item.Key));
    }

    [Fact]
    public void Window_WeekStartsOnSundayNoonEastern() {
        var (start, end) = CalendarLayout.Window(
            new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero), CalendarZoom.Week);

        var local = TimeZoneInfo.ConvertTime(start, CalendarLayout.GridZone);
        Assert.Equal(DayOfWeek.Sunday, local.DayOfWeek);
        Assert.Equal(new TimeSpan(12, 0, 0), local.TimeOfDay);
        Assert.Equal(7, (int)Math.Round((end - start).TotalDays));
    }

    [Fact]
    public void Rows_SlotToSlotSpan_NeverCutsAtAWeekBoundary() {
        var (start, end) = CalendarLayout.Window(
            new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero), CalendarZoom.Month);
        var monday = CalendarLayout.AddDays(start, 1);
        var items = new[] { Item("mon-to-sun", monday, CalendarLayout.AddDays(monday, 6)) };

        var rows = CalendarLayout.Rows(items, start, end, CalendarZoom.Month, start);
        var bars = rows.SelectMany(r => r.Lanes.SelectMany(l => l)).ToList();

        var bar = Assert.Single(bars);
        Assert.False(bar.ContinuesLeft);
        Assert.False(bar.ContinuesRight);
    }

    [Fact]
    public void Rows_MonthZoom_FiveWeekRowsWithOutOfMonthDaysMuted() {
        var (start, end) = CalendarLayout.Window(
            new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero), CalendarZoom.Month);

        var rows = CalendarLayout.Rows(Array.Empty<Span>(), start, end, CalendarZoom.Month, start);

        Assert.Equal(CalendarLayout.MonthWeeks, rows.Count);
        Assert.Equal(7, rows[0].Cells.Count);
        Assert.Contains(rows.SelectMany(r => r.Cells), c => c.Muted);
        Assert.Contains(rows.SelectMany(r => r.Cells), c => !c.Muted);
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
