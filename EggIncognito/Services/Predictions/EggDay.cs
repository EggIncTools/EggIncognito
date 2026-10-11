namespace EggIncognito.Services.Predictions;

public static class EggDay {
    public const int Month = 7;
    public const int Day = 14;

    public static bool Contains(double startUnix) {
        var date = NoonEastern.LocalDate(startUnix);
        return date.Month == Month && date.Day == Day;
    }
}
