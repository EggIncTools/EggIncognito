namespace EggIncognito.Models.Calendar;

public sealed record WatchEntry(
    string Key,
    string Label,
    bool Ultra,
    string? IconUrl,
    string BarStyle,
    double? LastStart,
    double? LastEnd,
    IReadOnlyList<WatchForecast> Next,
    string? LastNote = null,
    string? NoNextReason = null);

public sealed record WatchForecast(double Start, double End, double Confidence, string Rule, string? Note = null);

public sealed record WatchResponse(double GeneratedAt, IReadOnlyList<WatchEntry> Entries);
