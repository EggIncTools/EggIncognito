namespace EggIncognito.Models.Calendar;

public sealed record CalendarBar<T>(
    T Item,
    int Lane,
    double LeftPercent,
    double WidthPercent,
    bool Active,
    bool Past,
    bool ContinuesLeft,
    bool ContinuesRight) where T : ICalendarSpan;
