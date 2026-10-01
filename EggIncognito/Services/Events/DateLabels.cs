using System.Globalization;

namespace EggIncognito.Services.Events;

public static class DateLabels {
    public static string Ordinal(int day) {
        int rem100 = day % 100;
        if (rem100 is >= 11 and <= 13) return day + "th";
        return (day % 10) switch {
            1 => day + "st",
            2 => day + "nd",
            3 => day + "rd",
            _ => day + "th"
        };
    }

    public static string Weekday(DateTime date) => date.ToString("ddd", CultureInfo.InvariantCulture);

    public static string MonthDay(DateTime date) =>
        date.ToString("MMM", CultureInfo.InvariantCulture) + " " + Ordinal(date.Day);
}
