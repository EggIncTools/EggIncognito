using System.Globalization;
using EggIdentity.UI;
using EggIncognito.Models.Calendar;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions;

namespace EggIncognito.Services.Calendar;

public static class CalendarLayout {
    public const double DayGapFraction = 0.05;
    public const int MonthWeeks = 5;
    public const int WeeksBeforeCenter = 2;

    public static readonly TimeZoneInfo GridZone = NoonEastern.Zone;
    public static readonly TimeSpan GridAnchor = NoonEastern.Noon.ToTimeSpan();

    private const double MinWidthFraction = 0.006;
    private const double LaneTolerance = 1e-9;

    public static double GapPercent(DateTimeOffset start, DateTimeOffset end) =>
        100.0 / Math.Max(1, (end - start).TotalDays) * DayGapFraction;

    public static DateTimeOffset DayStart(DateTimeOffset instant) =>
        CalendarGridAnchor.DayStart(instant, GridZone, GridAnchor);

    public static DateTimeOffset WeekStart(DateTimeOffset instant) =>
        CalendarGridAnchor.WeekStart(instant, GridZone, GridAnchor);

    public static DateTimeOffset DayStartForDate(DateOnly date) =>
        CalendarGridAnchor.DayStartForDate(date, GridZone, GridAnchor);

    public static DateOnly GridDate(DateTimeOffset dayStart) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(dayStart, GridZone).DateTime);

    public static DateTimeOffset AddDays(DateTimeOffset dayStart, int days) =>
        DayStartForDate(GridDate(dayStart).AddDays(days));

    public static (DateTimeOffset Start, DateTimeOffset End) Window(DateTimeOffset center, CalendarZoom zoom) {
        var week = WeekStart(center);
        if (zoom == CalendarZoom.Week) return (week, AddDays(week, 7));
        var start = AddDays(week, -7 * WeeksBeforeCenter);
        return (start, AddDays(start, 7 * MonthWeeks));
    }

    public static int PrimaryMonth(DateTimeOffset visibleStart, DateTimeOffset visibleEnd) {
        var middle = visibleStart + (visibleEnd - visibleStart) / 2;
        return GridDate(DayStart(middle)).Month;
    }

    public static string RangeLabel(DateTimeOffset visibleStart, DateTimeOffset visibleEnd, CalendarZoom zoom) {
        if (zoom == CalendarZoom.Month) {
            var middle = visibleStart + (visibleEnd - visibleStart) / 2;
            var date = GridDate(DayStart(middle));
            return new DateTime(date.Year, date.Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        }

        var first = GridDate(visibleStart);
        var last = GridDate(AddDays(visibleEnd, -1));
        return first.ToString("MMM d", CultureInfo.InvariantCulture)
               + " - " + last.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }

    public static string CellLabel(CalendarCell cell, CalendarZoom zoom) =>
        zoom == CalendarZoom.Month
            ? cell.LocalDate.Day.ToString(CultureInfo.InvariantCulture)
            : cell.LocalDate.ToString("M/d", CultureInfo.InvariantCulture);

    public static IReadOnlyList<CalendarLayoutRow<T>> Rows<T>(
        IReadOnlyList<T> items,
        DateTimeOffset visibleStart,
        DateTimeOffset visibleEnd,
        CalendarZoom zoom,
        DateTimeOffset now) where T : ICalendarSpan {
        int? primaryMonth = zoom == CalendarZoom.Month ? PrimaryMonth(visibleStart, visibleEnd) : null;
        var rows = new List<CalendarLayoutRow<T>>();
        for (var week = WeekStart(visibleStart); week < visibleEnd; week = AddDays(week, 7)) {
            rows.Add(Row(items, week, AddDays(week, 7), now, primaryMonth));
        }

        return rows;
    }

    public static CalendarLayoutRow<T> Row<T>(
        IReadOnlyList<T> items,
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset now,
        int? primaryMonth = null) where T : ICalendarSpan {
        double? nowPercent = now > start && now < end
            ? (now - start).TotalSeconds / (end - start).TotalSeconds * 100
            : null;
        return new CalendarLayoutRow<T>(
            start, end, DayCells(start, end, primaryMonth), Lanes(Bars(items, start, end, now)), nowPercent);
    }

    private static List<CalendarCell> DayCells(DateTimeOffset start, DateTimeOffset end, int? primaryMonth) {
        var cells = new List<CalendarCell>();
        double span = (end - start).TotalSeconds;
        if (span <= 0) return cells;
        for (var day = DayStart(start); day < end; day = AddDays(day, 1)) {
            double left = Math.Max(0, (day - start).TotalSeconds / span * 100);
            var date = GridDate(day);
            cells.Add(new CalendarCell(left, date.ToDateTime(TimeOnly.MinValue), primaryMonth is { } month && date.Month != month));
        }

        return cells;
    }

    private static List<CalendarBar<T>> Bars<T>(
        IReadOnlyList<T> items, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now) where T : ICalendarSpan {
        double windowStart = UnixSeconds.FromTime(start);
        double windowEnd = UnixSeconds.FromTime(end);
        double nowUnix = UnixSeconds.FromTime(now);
        double span = windowEnd - windowStart;
        var hits = items
            .Where(i => i.End > windowStart && i.Start < windowEnd)
            .OrderBy(i => i.Start)
            .ThenByDescending(i => i.End - i.Start)
            .ThenBy(i => i.Key, StringComparer.Ordinal)
            .ToList();
        var laneRights = new List<double>();
        var bars = new List<CalendarBar<T>>(hits.Count);
        foreach (var item in hits) {
            var (left, width) = Clip(item.Start, item.End, windowStart, span);
            bool past = item.End <= nowUnix;
            bars.Add(new CalendarBar<T>(
                item,
                CalendarLanePacker.AssignLane(laneRights, left, left + width, LaneTolerance),
                left * 100,
                width * 100,
                !past && item.Start <= nowUnix,
                past,
                item.Start < windowStart,
                item.End > windowEnd));
        }

        return bars;
    }

    private static List<IReadOnlyList<CalendarBar<T>>> Lanes<T>(List<CalendarBar<T>> bars) where T : ICalendarSpan =>
        [.. bars.GroupBy(b => b.Lane).OrderBy(g => g.Key).Select(lane => (IReadOnlyList<CalendarBar<T>>)[.. lane])];

    private static (double Left, double Width) Clip(double startUnix, double endUnix, double windowStart, double span) {
        if (span <= 0) return (0, 1);
        double left = Math.Max(0, (startUnix - windowStart) / span);
        double right = Math.Min(1, (endUnix - windowStart) / span);
        double width = Math.Max(0, right - left);
        if (width >= MinWidthFraction) return (left, width);
        width = Math.Min(MinWidthFraction, 1);
        return (Math.Min(left, 1 - width), width);
    }
}
