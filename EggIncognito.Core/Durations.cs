namespace EggIncognito.Core;

public static class Durations {
    public static string Format(TimeSpan span) {
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {(int)(span.TotalMinutes % 60)}m";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m";
        return span.TotalSeconds < 1 ? "0s" : $"{(int)span.TotalSeconds}s";
    }
}
