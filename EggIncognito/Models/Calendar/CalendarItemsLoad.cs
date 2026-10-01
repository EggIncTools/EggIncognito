namespace EggIncognito.Models.Calendar;

public sealed record CalendarItemsLoad<T>(IReadOnlyList<T> Items, string Note) where T : ICalendarSpan;

public static class CalendarItemsLoad {
    public static CalendarItemsLoad<T> Ok<T>(IReadOnlyList<T> items) where T : ICalendarSpan => new(items, "");

    public static CalendarItemsLoad<T> Failed<T>(string note) where T : ICalendarSpan => new([], note);
}
