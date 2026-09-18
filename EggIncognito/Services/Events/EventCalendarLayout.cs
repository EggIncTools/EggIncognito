using EggIdentity.UI;
using EggIncognito.Models.Events;

namespace EggIncognito.Services.Events;

public static class EventCalendarLayout {
    public const double DayGapFraction = 0.05;

    private const double MinWidthFraction = 0.006;

    public static double GapPercent(DateTimeOffset start, DateTimeOffset end) =>
        100.0 / Math.Max(1, (end - start).TotalDays) * DayGapFraction;

    public static (DateTimeOffset Start, DateTimeOffset End) Window(
        DateTimeOffset center, EventCalendarZoom zoom, TimeZoneInfo zone) {
        var local = TimeZoneInfo.ConvertTime(center, zone).DateTime;
        if (zoom == EventCalendarZoom.Week) {
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

    public static IReadOnlyList<EventCalendarRow> Rows(
        IReadOnlyList<CalendarItem> items,
        DateTimeOffset visibleStart,
        DateTimeOffset visibleEnd,
        EventCalendarZoom zoom,
        DateTimeOffset now,
        TimeZoneInfo zone) {
        int? primaryMonth = zoom == EventCalendarZoom.Month
            ? TimeZoneInfo.ConvertTime(visibleStart, zone).Month
            : null;
        return RowSpans(visibleStart, visibleEnd, zoom, zone)
            .Select(span => BuildRow(items, span.Start, span.End, now, primaryMonth, zone))
            .ToList();
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> RowSpans(
        DateTimeOffset visibleStart, DateTimeOffset visibleEnd, EventCalendarZoom zoom, TimeZoneInfo zone) {
        if (zoom == EventCalendarZoom.Week) return [(visibleStart, visibleEnd)];
        var spans = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        for (var day = WeekStart(TimeZoneInfo.ConvertTime(visibleStart, zone).DateTime);
             ToOffset(day, zone) < visibleEnd;
             day = day.AddDays(7)) {
            spans.Add((ToOffset(day, zone), ToOffset(day.AddDays(7), zone)));
        }

        return spans;
    }

    private static EventCalendarRow BuildRow(
        IReadOnlyList<CalendarItem> items,
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset now,
        int? primaryMonth,
        TimeZoneInfo zone) {
        double? nowPercent = now > start && now < end
            ? (now - start).TotalSeconds / (end - start).TotalSeconds * 100
            : null;
        return new EventCalendarRow(
            start, end, DayCells(start, end, primaryMonth, zone), Lanes(Bars(items, start, end, now)), nowPercent);
    }

    private static List<EventCalendarCell> DayCells(
        DateTimeOffset start, DateTimeOffset end, int? primaryMonth, TimeZoneInfo zone) {
        var cells = new List<EventCalendarCell>();
        double span = (end - start).TotalSeconds;
        if (span <= 0) return cells;
        for (var day = TimeZoneInfo.ConvertTime(start, zone).DateTime.Date; ToOffset(day, zone) < end; day = day.AddDays(1)) {
            double left = Math.Max(0, (ToOffset(day, zone) - start).TotalSeconds / span * 100);
            cells.Add(new EventCalendarCell(left, day, primaryMonth is { } month && day.Month != month));
        }

        return cells;
    }

    private static List<EventCalendarBar> Bars(
        IReadOnlyList<CalendarItem> items, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now) {
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
        double laneGap = DayGapFraction / Math.Max(1, (end - start).TotalDays);
        var laneRights = new List<double>();
        var bars = new List<EventCalendarBar>(hits.Count);
        foreach (var item in hits) {
            var (left, width) = Clip(item.Start, item.End, windowStart, span);
            bool past = item.End <= nowUnix;
            bars.Add(new EventCalendarBar(
                item,
                AssignLane(laneRights, left, left + width, laneGap),
                left * 100,
                width * 100,
                !past && item.Start <= nowUnix,
                past,
                item.Start < windowStart,
                item.End > windowEnd));
        }

        return bars;
    }

    private static List<IReadOnlyList<EventCalendarBar>> Lanes(List<EventCalendarBar> bars) {
        return [.. bars.GroupBy(b => b.Lane).OrderBy(g => g.Key).Select(lane => (IReadOnlyList<EventCalendarBar>)[.. lane])];
    }

    private static int AssignLane(List<double> laneRights, double left, double right, double gap) {
        return CalendarLanePacker.AssignLane(laneRights, left, right, -gap);
    }

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
