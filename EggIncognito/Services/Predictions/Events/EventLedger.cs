using System.Globalization;

namespace EggIncognito.Services.Predictions.Events;

public sealed class EventLedger {
    public Dictionary<(string Type, bool Ultra), double> LastStart { get; } = [];

    public Dictionary<(string Type, bool Ultra), DateOnly> LastDate { get; } = [];

    public Dictionary<(string Type, bool Ultra), double> GapDays { get; } = [];

    public Dictionary<(string Type, bool Ultra), int> GapSamples { get; } = [];

    public HashSet<(int Year, int Week, string Type, bool Ultra)> WeekUsed { get; } = [];

    public Dictionary<(int Year, int Week, string Type, bool Ultra), double> WeekGuessed { get; } = [];

    public Dictionary<(string Type, bool Ultra), (DateOnly Date, double Start, double Confidence)> LastGuess { get; } = [];

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

    public void Touch(string type, bool ultra, double start, DateOnly date) {
        LastStart[(type, ultra)] = start;
        LastDate[(type, ultra)] = date;
        WeekUsed.Add(WeekKey(date, type, ultra));
    }

    public void Guess(string type, bool ultra, DateOnly date, double confidence) {
        LastGuess[(type, ultra)] = (date, NoonEastern.SlotTime(date), confidence);
        var key = WeekKey(date, type, ultra);
        WeekGuessed[key] = Math.Max(WeekGuessed.GetValueOrDefault(key), confidence);
    }

    public bool Repeats(string type, bool ultra, DateOnly date, int blockDays) =>
        LastDate.TryGetValue((type, ultra), out var last) && date.DayNumber - last.DayNumber <= blockDays;

    public double GuessedRepeatWeight(string type, bool ultra, DateOnly date, int blockDays) =>
        LastGuess.TryGetValue((type, ultra), out var guess) && date.DayNumber - guess.Date.DayNumber <= blockDays
            ? 1 - guess.Confidence
            : 1;

    public double GuessedDueWeight(string type, bool ultra, double at) {
        if (!LastGuess.TryGetValue((type, ultra), out var guess)) return 1;
        if (!GapDays.TryGetValue((type, ultra), out double gap) || gap <= 0) return 1;
        double sinceGuess = (at - guess.Start) / EventHistory.Day / gap;
        if (sinceGuess >= 1) return 1;
        return 1 - guess.Confidence * (1 - sinceGuess);
    }

    public double WeekWeight(DateOnly date, string type, bool ultra, double penalty) {
        var key = WeekKey(date, type, ultra);
        if (WeekUsed.Contains(key)) return penalty;
        return WeekGuessed.TryGetValue(key, out double confidence) ? 1 - confidence * (1 - penalty) : 1;
    }

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
