using EggIncognito.Services.Events;

namespace EggIncognito.Services.Predictions.Events;

public sealed class EventHistory {
    public const double Day = 86400d;
    public const int WindowDays = 182;
    private const double MinUltraSeconds = 12 * 3600d;

    private EventHistory(
        IReadOnlyList<DateOnly> days, IReadOnlyList<EventOccurrence> standard, IReadOnlyList<EventOccurrence> ultra) {
        Days = days;
        Standard = standard;
        Ultra = ultra;
    }

    public IReadOnlyList<DateOnly> Days { get; }

    public IReadOnlyList<EventOccurrence> Standard { get; }

    public IReadOnlyList<EventOccurrence> Ultra { get; }

    public IEnumerable<EventOccurrence> All => Standard.Concat(Ultra);

    public bool Empty => Days.Count == 0;

    public DateOnly First => Days[0];

    public DateOnly Last => Days[^1];

    public int WeekdayCount(DayOfWeek weekday) => Days.Count(d => d.DayOfWeek == weekday);

    public static EventHistory Build(IReadOnlyList<EventRow> rows, double asOf) {
        var days = WindowDates(asOf - WindowDays * Day, asOf);
        if (days.Count == 0) return new EventHistory([], [], []);
        var window = Collapse(rows, asOf, days[0], days[^1]);
        return new EventHistory(
            days,
            [.. window.Where(o => !o.Ultra)],
            [.. window.Where(o => o.Ultra && o.Duration >= MinUltraSeconds)]);
    }

    private static List<EventOccurrence> Collapse(
        IReadOnlyList<EventRow> rows, double asOf, DateOnly min, DateOnly max) {
        var kept = new Dictionary<(string Type, bool Ultra, DateOnly Date), EventOccurrence>();
        foreach (var row in rows.Where(r => r.Start < asOf).OrderBy(r => r.Start)) {
            if (!UnixSeconds.IsValid(row.Start) || !UnixSeconds.IsValid(row.End)) continue;
            var date = NoonEastern.LocalDate(row.Start);
            if (date < min || date > max) continue;
            kept.TryAdd(
                (row.Type, row.Ultra, date),
                new EventOccurrence(row.Type, row.Ultra, date, row.Start, Math.Max(row.End - row.Start, 0)));
        }

        return [.. kept.Values.OrderBy(o => o.Start)];
    }

    private static List<DateOnly> WindowDates(double windowStart, double asOf) {
        var dates = new List<DateOnly>();
        if (!UnixSeconds.IsValid(windowStart) || !UnixSeconds.IsValid(asOf)) return dates;
        var last = NoonEastern.LocalDate(asOf).AddDays(2);
        for (var day = NoonEastern.LocalDate(windowStart).AddDays(-2); day <= last; day = day.AddDays(1)) {
            double time = NoonEastern.SlotTime(day);
            if (time >= windowStart && time < asOf) dates.Add(day);
        }

        return dates;
    }
}
