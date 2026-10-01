using System.Globalization;
using EggIdentity.UI;
using EggIncognito.Models.Calendar;
using EggIncognito.Services.Events;

namespace EggIncognito.Services.Calendar;

public static class CalendarLayout {
    public const double DayGapFraction = 0.05;

    private const double MinWidthFraction = 0.006;
    private const double LaneTolerance = 1e-9;

    public static double GapPercent(DateTimeOffset start, DateTimeOffset end) =>
        100.0 / Math.Max(1, (end - start).TotalDays) * DayGapFraction;

    public static (DateTimeOffset Start, DateTimeOffset End) Window(
        DateTimeOffset center, CalendarZoom zoom, TimeZoneInfo zone) {
        var local = TimeZoneInfo.ConvertTime(center, zone).DateTime;
        if (zoom == CalendarZoom.Week) {
            var weekStart = WeekStart(local);
            return (ToOffset(weekStart, zone), ToOffset(weekStart.AddDays(7), zone));
        }

        var monthStart = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return (ToOffset(monthStart, zone), ToOffset(monthStart.AddMonths(1), zone));
    }

    public static DateTimeOffset ToOffset(DateTime local, TimeZoneInfo zone) {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }

    public static string RangeLabel(
        DateTimeOffset visibleStart, DateTimeOffset visibleEnd, CalendarZoom zoom, TimeZoneInfo zone) {
        var start = TimeZoneInfo.ConvertTime(visibleStart, zone);
        if (zoom == CalendarZoom.Month) return start.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var end = TimeZoneInfo.ConvertTime(visibleEnd, zone).AddSeconds(-1);
        return start.ToString("MMM d", CultureInfo.InvariantCulture)
               + " - " + end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
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
        DateTimeOffset now,
        TimeZoneInfo zone) where T : ICalendarSpan {
        int? primaryMonth = zoom == CalendarZoom.Month
            ? TimeZoneInfo.ConvertTime(visibleStart, zone).Month
            : null;
        return RowSpans(visibleStart, visibleEnd, zoom, zone)
            .Select(span => BuildRow(items, span.Start, span.End, now, primaryMonth, zone))
            .ToList();
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> RowSpans(
        DateTimeOffset visibleStart, DateTimeOffset visibleEnd, CalendarZoom zoom, TimeZoneInfo zone) {
        if (zoom == CalendarZoom.Week) return [(visibleStart, visibleEnd)];
        var spans = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        for (var day = WeekStart(TimeZoneInfo.ConvertTime(visibleStart, zone).DateTime);
             ToOffset(day, zone) < visibleEnd;
             day = day.AddDays(7)) {
            spans.Add((ToOffset(day, zone), ToOffset(day.AddDays(7), zone)));
        }

        return spans;
    }

    private static CalendarLayoutRow<T> BuildRow<T>(
        IReadOnlyList<T> items,
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset now,
        int? primaryMonth,
        TimeZoneInfo zone) where T : ICalendarSpan {
        double? nowPercent = now > start && now < end
            ? (now - start).TotalSeconds / (end - start).TotalSeconds * 100
            : null;
        return new CalendarLayoutRow<T>(
            start, end, DayCells(start, end, primaryMonth, zone), Lanes(Bars(items, start, end, now)), nowPercent);
    }

    private static List<CalendarCell> DayCells(
        DateTimeOffset start, DateTimeOffset end, int? primaryMonth, TimeZoneInfo zone) {
        var cells = new List<CalendarCell>();
        double span = (end - start).TotalSeconds;
        if (span <= 0) return cells;
        for (var day = TimeZoneInfo.ConvertTime(start, zone).DateTime.Date; ToOffset(day, zone) < end; day = day.AddDays(1)) {
            double left = Math.Max(0, (ToOffset(day, zone) - start).TotalSeconds / span * 100);
            cells.Add(new CalendarCell(left, day, primaryMonth is { } month && day.Month != month));
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

    private static DateTime WeekStart(DateTime local) =>
        CalendarGridAnchor.WeekStartDate(DateOnly.FromDateTime(local)).ToDateTime(TimeOnly.MinValue);
}
