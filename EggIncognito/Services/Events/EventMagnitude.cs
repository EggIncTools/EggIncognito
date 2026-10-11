using System.Globalization;
using EggIncognito.Models.Events;

namespace EggIncognito.Services.Events;

public static class EventMagnitude {
    public static string Scalar(GameEventDto e) => Lead(e.Message) ?? Describe(e);

    public static string Describe(GameEventDto e) => Describe(e.Multiplier);

    public static string Describe(double multiplier) {
        if (multiplier <= 0) return "";
        if (multiplier < 1) return Percent(1 - multiplier) + " off";
        return multiplier.ToString("0.###", CultureInfo.InvariantCulture) + "x";
    }

    private static string Percent(double fraction) =>
        (fraction * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string? Lead(string? message) {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var token = message.TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (token is null || !char.IsAsciiDigit(token[0])) return null;
        return token.TrimEnd('!', ',', '.');
    }
}
