namespace EggIncognito.Models.Calendar;

public sealed record CalendarLayoutRow<T>(
    DateTimeOffset Start,
    DateTimeOffset End,
    IReadOnlyList<CalendarCell> Cells,
    IReadOnlyList<IReadOnlyList<CalendarBar<T>>> Lanes,
    double? NowPercent) where T : ICalendarSpan;
