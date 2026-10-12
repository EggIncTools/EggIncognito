using System.Globalization;

namespace EggIncognito.Services.Predictions.Events;

public sealed class EventLedger {
    public Dictionary<(string Type, bool Ultra), double> LastStart { get; private init; } = [];

    public Dictionary<(string Type, bool Ultra), DateOnly> LastDate { get; private init; } = [];

    public Dictionary<(string Type, bool Ultra), double> GapDays { get; private init; } = [];

    public Dictionary<(string Type, bool Ultra), int> GapSamples { get; private init; } = [];

    public HashSet<(int Year, int Week, string Type, bool Ultra)> WeekUsed { get; private init; } = [];

    public static EventLedger From(EventHistory history) {
        var ledger = new EventLedger();
        foreach (var group in history.All.GroupBy(o => (o.Type, o.Ultra))) {
            var ordered = group.OrderBy(o => o.Start).ToList();
            ledger.LastStart[group.Key] = ordered[^1].Start;
            ledger.LastDate[group.Key] = ordered[^1].Date;
            foreach (var o in ordered) ledger.WeekUsed.Add(WeekKey(o.Date, o.Type, o.Ultra));
            var gaps = new List<double>(ordered.Count);
            for (int i = 1; i < ordered.Count; i++) gaps.Add(ordered[i].Date.DayNumber - ordered[i - 1].Date.DayNumber);
            ledger.GapSamples[group.Key] = gaps.Count;
            if (gaps.Count > 0) ledger.GapDays[group.Key] = RobustStats.Median(gaps);
        }

        return ledger;
    }

    public EventLedger Clone() => new() {
        LastStart = LastStart.ToDictionary(),
        LastDate = LastDate.ToDictionary(),
        GapDays = GapDays,
        GapSamples = GapSamples,
        WeekUsed = [.. WeekUsed]
    };

    public void Touch(string type, bool ultra, double start, DateOnly date) {
        LastStart[(type, ultra)] = start;
        LastDate[(type, ultra)] = date;
        WeekUsed.Add(WeekKey(date, type, ultra));
    }

    public bool Repeats(string type, bool ultra, DateOnly date, int blockDays) =>
        LastDate.TryGetValue((type, ultra), out var last) && date.DayNumber - last.DayNumber <= blockDays;

    public double WeekWeight(DateOnly date, string type, bool ultra, double penalty) =>
        WeekUsed.Contains(WeekKey(date, type, ultra)) ? penalty : 1;

    public double DaysSince(string type, bool ultra, double at) =>
        LastStart.TryGetValue((type, ultra), out double last) ? (at - last) / EventHistory.Day : double.MaxValue;

    public double Due(string type, bool ultra, double at, double min, double max, int minSamples = 1) {
        if (!LastStart.TryGetValue((type, ultra), out double last)) return max;
        if (!GapDays.TryGetValue((type, ultra), out double gap) || gap <= 0) return 1;
        if (GapSamples.GetValueOrDefault((type, ultra)) < minSamples) return 1;
        return Math.Clamp((at - last) / EventHistory.Day / gap, min, max);
    }

    public static (int Year, int Week, string Type, bool Ultra) WeekKey(DateOnly date, string type, bool ultra) {
        var day = date.ToDateTime(TimeOnly.MinValue);
        return (ISOWeek.GetYear(day), ISOWeek.GetWeekOfYear(day), type, ultra);
    }
}
