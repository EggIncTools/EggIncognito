using EggIncognito.Services.Events;

namespace EggIncognito.Services.Predictions;

public static class NoonEastern {
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    public static readonly TimeOnly Noon = new(12, 0);

    public static double SlotTime(DateOnly day) => SlotTime(day, Noon);

    public static double SlotTime(DateOnly day, TimeOnly time) =>
        UnixSeconds.FromTime(new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(time), Zone)));

    public static DateOnly LocalDate(double unix) => LocalDate(UnixSeconds.ToTime(unix));

    public static DateOnly LocalDate(DateTimeOffset time) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, Zone).DateTime);

    public static TimeOnly LocalTime(double unix) =>
        TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(UnixSeconds.ToTime(unix), Zone).DateTime);
}
