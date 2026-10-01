namespace EggIncognito.Services.Predictions.Events;

public static class RuleDates {
    public static IEnumerable<DateOnly> Future(DateOnly anchor, int period, double asOf, double horizonEnd) {
        var date = anchor.AddDays(period);
        while (NoonEastern.SlotTime(date) < horizonEnd) {
            if (NoonEastern.SlotTime(date) >= asOf) yield return date;
            date = date.AddDays(period);
        }
    }

    public static IEnumerable<DateOnly> FutureDays(double asOf, double horizonEnd) {
        var date = NoonEastern.LocalDate(asOf);
        while (NoonEastern.SlotTime(date) < horizonEnd) {
            if (NoonEastern.SlotTime(date) >= asOf) yield return date;
            date = date.AddDays(1);
        }
    }

    public static string Weekday(DayOfWeek day) => day.ToString();
}
