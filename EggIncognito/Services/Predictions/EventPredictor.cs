using System.Globalization;
using EggIncognito.Data.Services;
using EggIncognito.Models.Events;
using EggIncognito.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Predictions;

public readonly record struct EventRow(string Type, bool Ultra, double Start, double End);

public sealed class EventPredictor(EggIncognitoDbContext db, EventDataVersion version, EventPredictionCache cache,
    TimeProvider time) {
    internal const double Day = 86400d;
    internal const int WindowDays = 182;
    internal const int MinHorizonDays = 1;
    internal const int MaxHorizonDays = 90;
    internal const int MaxCandidates = 5;
    internal const int MinLaneSamples = 4;
    internal const int MaxOffGrid = 1;
    internal const int PoolPeriodDays = 1;
    internal const double MinLaneFill = 0.8;
    internal const double MinUltraSeconds = 12 * 3600d;
    internal const double MaxUltraJitterDays = 0.5;
    internal const double MinDue = 0.25;
    internal const double MaxDue = 2d;
    internal const double Smoothing = 0.5;
    internal const int RepeatBlockDays = 1;
    internal const double SameWeekPenalty = 0.5;
    internal const double MinDayLength = 0.75 * Day;
    internal const double MaxDayLength = 1.5 * Day;

    private static readonly int[] FixedPeriods = [7, 14, 28];
    private static readonly Dictionary<string, double> NoWeights = [];

    public async Task<EventPredictionSet> GetAsync(
        int horizonDays = 28, double? asOf = null, CancellationToken ct = default) {
        var rows = await RowsAsync(ct);
        double at = asOf ?? UnixSeconds.FromTime(time.GetUtcNow());
        return new EventPredictionSet(at, Predict(rows, at, horizonDays));
    }

    public async Task<IReadOnlyList<EventRow>> RowsAsync(CancellationToken ct = default) {
        long v = version.Version;
        if (cache.Version == v && cache.Value is { } cached) return cached;

        var rows = await db.GameEvents.AsNoTracking()
            .OrderBy(e => e.StartTime)
            .Select(e => new { e.EventType, e.Ultra, e.StartTime, e.EndTime })
            .ToListAsync(ct);
        var value = rows
            .Select(r => new EventRow(
                r.EventType, r.Ultra, UnixSeconds.FromTime(r.StartTime), UnixSeconds.FromTime(r.EndTime)))
            .ToList();

        lock (cache) {
            cache.Value = value;
            cache.Version = v;
        }
        return value;
    }

    public static IReadOnlyList<EventPrediction> Predict(
        IReadOnlyList<EventRow> rows, double asOf, int horizonDays) {
        int horizon = Math.Clamp(horizonDays, MinHorizonDays, MaxHorizonDays);
        double horizonEnd = asOf + horizon * Day;
        var days = WindowDates(asOf - WindowDays * Day, asOf);
        if (days.Count == 0) return [];

        var window = Collapse(rows, asOf, days[0], days[^1]);
        var standard = window.Where(o => !o.Ultra).ToList();
        var ultra = window.Where(o => o.Ultra && o.Duration >= MinUltraSeconds).ToList();

        var lanes = FixedLanes(standard, days);
        var claimed = lanes.Select(l => (l.Type, l.Weekday)).ToHashSet();
        var unclaimed = standard.Where(o => !claimed.Contains((o.Type, o.Date.DayOfWeek))).ToList();
        var slots = PoolSlots(unclaimed, days, lanes);
        var ultraLane = UltraLaneFor(ultra, days);

        var history = BuildHistory(window, ultra);
        var poolTypes = Types(unclaimed);
        var poolWeights = WeightsByWeekday(unclaimed);
        var ultraTypes = Types(ultra);
        var ultraWeights = Weights(ultra);
        var predictions = new List<EventPrediction>();
        var used = new HashSet<(string Key, double Start)>();

        foreach (var plan in Plans(lanes, slots, ultraLane, asOf, horizonEnd)) {
            if (plan.Lane is { } lane) {
                if (!used.Add((LaneKey(lane), plan.Start))) continue;
                predictions.Add(new EventPrediction(
                    lane.Type, false, EventPredictionKind.Fixed, plan.Start, plan.Start + lane.Duration,
                    Fill(lane.Observed, lane.Expected), [new EventCandidate(lane.Type, 1)],
                    lane.Observed, lane.Expected, lane.Period, lane.LastStart));
                Touch(history, lane.Type, false, plan.Start, plan.Date);
            } else if (plan.Slot is { } slot) {
                if (!used.Add(("pool", plan.Start))) continue;
                var candidates = Score(
                    poolTypes, poolWeights.GetValueOrDefault(slot.Weekday, NoWeights), slot.Expected,
                    history, false, plan.Start, plan.Date, null);
                var top = candidates.Count > 0 ? candidates[0] : null;
                predictions.Add(new EventPrediction(
                    top?.Type, false, EventPredictionKind.Pool, plan.Start, plan.Start + slot.Duration,
                    top?.Probability ?? 0, candidates, slot.Observed, slot.Expected,
                    PoolPeriodDays, slot.LastStart));
                if (top is not null) Touch(history, top.Type, false, plan.Start, plan.Date);
            } else if (plan.Ultra is { } lane2) {
                if (!used.Add(("ultra", plan.Start))) continue;
                var candidates = Score(
                    ultraTypes, ultraWeights, ultra.Count,
                    history, true, plan.Start, plan.Date, history.LastUltraType);
                var top = candidates.Count > 0 ? candidates[0] : null;
                predictions.Add(new EventPrediction(
                    top?.Type, true, EventPredictionKind.Ultra, plan.Start, plan.Start + lane2.Duration,
                    top?.Probability ?? 0, candidates, lane2.Observed, lane2.Expected,
                    lane2.Period, lane2.LastStart));
                if (top is null) continue;
                Touch(history, top.Type, true, plan.Start, plan.Date);
                history.LastUltraType = top.Type;
            }
        }
        return predictions;
    }

    private static List<FixedLane> FixedLanes(List<Occurrence> standard, List<DateOnly> days) {
        var lanes = new List<FixedLane>();
        foreach (var group in standard.GroupBy(o => (o.Type, o.Date.DayOfWeek))) {
            var occurrences = group.OrderBy(o => o.Start).ToList();
            var dates = occurrences.Select(o => o.Date).ToHashSet();
            var anchor = occurrences[^1].Date;
            foreach (int period in FixedPeriods) {
                var grid = GridDates(anchor, period, days[0], days[^1]);
                if (grid.Count < MinLaneSamples) continue;
                int observed = grid.Count(dates.Contains);
                if (observed < MinLaneFill * grid.Count && !HeldRecently(anchor, period, dates, days[^1])) continue;
                if (dates.Count(d => !grid.Contains(d)) > MaxOffGrid) continue;
                lanes.Add(new FixedLane(
                    group.Key.Type, group.Key.DayOfWeek, period, anchor, grid, observed, grid.Count,
                    RobustStats.Median(occurrences.Select(o => o.Duration).ToList()), occurrences[^1].Start));
                break;
            }
        }
        return lanes;
    }

    private static bool HeldRecently(DateOnly anchor, int period, HashSet<DateOnly> dates, DateOnly last) {
        if (anchor.AddDays(period) <= last) return false;
        int run = 0;
        for (var d = anchor; dates.Contains(d); d = d.AddDays(-period)) run++;
        return run >= MinLaneSamples && run * period * 2 >= WindowDays;
    }

    private static HashSet<DateOnly> GridDates(DateOnly anchor, int period, DateOnly min, DateOnly max) {
        var grid = new HashSet<DateOnly>();
        for (var d = anchor; d >= min; d = d.AddDays(-period)) grid.Add(d);
        for (var d = anchor.AddDays(period); d <= max; d = d.AddDays(period)) grid.Add(d);
        return grid;
    }

    private static Dictionary<DayOfWeek, PoolSlot> PoolSlots(
        List<Occurrence> unclaimed, List<DateOnly> days, List<FixedLane> lanes) {
        var byDate = unclaimed.GroupBy(o => o.Date).ToDictionary(g => g.Key, g => g.ToList());
        var slots = new Dictionary<DayOfWeek, PoolSlot>();
        foreach (var group in days.GroupBy(d => d.DayOfWeek)) {
            var weekdays = group.ToList();
            if (weekdays.Count < MinLaneSamples) continue;
            int observed = weekdays.Count(byDate.ContainsKey);
            if (observed < MinLaneFill * weekdays.Count) continue;
            if (lanes.Exists(l => Supplies(l, group.Key, byDate))) continue;
            var occurrences = weekdays.Where(byDate.ContainsKey).SelectMany(d => byDate[d]).ToList();
            slots[group.Key] = new PoolSlot(
                group.Key, observed, weekdays.Count,
                RobustStats.Median(occurrences.Select(o => o.Duration).ToList()),
                occurrences.Max(o => o.Start));
        }
        return slots;
    }

    private static bool Supplies(FixedLane lane, DayOfWeek weekday, Dictionary<DateOnly, List<Occurrence>> byDate) =>
        lane.Weekday == weekday
        && lane.Duration >= MinDayLength && lane.Duration <= MaxDayLength
        && !lane.Grid.Any(byDate.ContainsKey);

    private static UltraLane? UltraLaneFor(List<Occurrence> ultra, List<DateOnly> days) {
        var dates = ultra.Select(o => o.Date).Distinct().Order().ToList();
        if (dates.Count < MinLaneSamples) return null;

        var intervals = new List<double>(dates.Count - 1);
        for (int i = 1; i < dates.Count; i++) intervals.Add(dates[i].DayNumber - dates[i - 1].DayNumber);
        double median = RobustStats.Median(intervals);
        if (median <= 0 || RobustStats.Mad(intervals, median) > MaxUltraJitterDays) return null;

        int period = Math.Max((int)Math.Round(median), 1);
        int expected = (int)Math.Round(days.Count / (double)period);
        if (expected < MinLaneSamples || dates.Count < MinLaneFill * expected) return null;
        return new UltraLane(
            period, dates[^1], dates.Count, expected,
            RobustStats.Median(ultra.Select(o => o.Duration).ToList()), ultra.Max(o => o.Start));
    }

    private static List<Plan> Plans(
        List<FixedLane> lanes, Dictionary<DayOfWeek, PoolSlot> slots, UltraLane? ultra,
        double asOf, double horizonEnd) {
        var plans = new List<Plan>();
        foreach (var lane in lanes) {
            foreach (var date in Future(lane.Anchor, lane.Period, asOf, horizonEnd))
                plans.Add(new Plan(NoonEastern.SlotTime(date), date, lane, null, null));
        }
        if (slots.Count > 0) {
            foreach (var date in FutureDays(asOf, horizonEnd)) {
                if (slots.TryGetValue(date.DayOfWeek, out var slot))
                    plans.Add(new Plan(NoonEastern.SlotTime(date), date, null, slot, null));
            }
        }
        if (ultra is { } lane2) {
            foreach (var date in Future(lane2.Anchor, lane2.Period, asOf, horizonEnd))
                plans.Add(new Plan(NoonEastern.SlotTime(date), date, null, null, lane2));
        }
        return [
            .. plans
                .OrderBy(p => p.Start)
                .ThenBy(p => p.Rank)
                .ThenBy(p => p.Lane?.Type ?? "", StringComparer.Ordinal)
        ];
    }

    private static IEnumerable<DateOnly> Future(DateOnly anchor, int period, double asOf, double horizonEnd) {
        var date = anchor.AddDays(period);
        while (NoonEastern.SlotTime(date) < horizonEnd) {
            if (NoonEastern.SlotTime(date) >= asOf) yield return date;
            date = date.AddDays(period);
        }
    }

    private static IEnumerable<DateOnly> FutureDays(double asOf, double horizonEnd) {
        var date = NoonEastern.LocalDate(asOf);
        while (NoonEastern.SlotTime(date) < horizonEnd) {
            if (NoonEastern.SlotTime(date) >= asOf) yield return date;
            date = date.AddDays(1);
        }
    }

    private static List<EventCandidate> Score(
        List<string> types, Dictionary<string, double> weights, double total,
        History history, bool ultra, double at, DateOnly date, string? blocked) {
        var scores = new List<(string Type, double Score)>(types.Count);
        double sum = 0;
        foreach (string type in types) {
            double score = 0;
            if (!string.Equals(type, blocked, StringComparison.Ordinal) && !Repeats(history, type, ultra, date)) {
                double prior = (weights.GetValueOrDefault(type) + Smoothing) / (total + Smoothing * types.Count);
                score = prior * Due(history, type, ultra, at);
                if (history.WeekUsed.Contains(WeekKey(date, type, ultra))) score *= SameWeekPenalty;
            }
            scores.Add((type, score));
            sum += score;
        }
        if (sum <= 0) return [];
        return [
            .. scores
                .Where(s => s.Score > 0)
                .Select(s => new EventCandidate(s.Type, s.Score / sum))
                .OrderByDescending(c => c.Probability)
                .ThenBy(c => c.Type, StringComparer.Ordinal)
                .Take(MaxCandidates)
        ];
    }

    private static bool Repeats(History history, string type, bool ultra, DateOnly date) =>
        history.LastDate.TryGetValue((type, ultra), out var last)
        && date.DayNumber - last.DayNumber <= RepeatBlockDays;

    private static double Due(History history, string type, bool ultra, double at) {
        if (!history.LastStart.TryGetValue((type, ultra), out double last)) return MaxDue;
        if (!history.GapDays.TryGetValue((type, ultra), out double gap) || gap <= 0) return 1;
        return Math.Clamp((at - last) / Day / gap, MinDue, MaxDue);
    }

    private static void Touch(History history, string type, bool ultra, double start, DateOnly date) {
        history.LastStart[(type, ultra)] = start;
        history.LastDate[(type, ultra)] = date;
        history.WeekUsed.Add(WeekKey(date, type, ultra));
    }

    private static (int Year, int Week, string Type, bool Ultra) WeekKey(DateOnly date, string type, bool ultra) {
        var day = date.ToDateTime(TimeOnly.MinValue);
        return (ISOWeek.GetYear(day), ISOWeek.GetWeekOfYear(day), type, ultra);
    }

    private static History BuildHistory(List<Occurrence> window, List<Occurrence> ultra) {
        var history = new History();
        foreach (var group in window.GroupBy(o => (o.Type, o.Ultra))) {
            var ordered = group.OrderBy(o => o.Start).ToList();
            history.LastStart[group.Key] = ordered[^1].Start;
            history.LastDate[group.Key] = ordered[^1].Date;
            var gaps = new List<double>(ordered.Count - 1);
            for (int i = 1; i < ordered.Count; i++)
                gaps.Add(ordered[i].Date.DayNumber - ordered[i - 1].Date.DayNumber);
            if (gaps.Count > 0) history.GapDays[group.Key] = RobustStats.Median(gaps);
        }
        if (ultra.Count > 0) history.LastUltraType = ultra.OrderBy(o => o.Start).ToList()[^1].Type;
        return history;
    }

    private static List<Occurrence> Collapse(
        IReadOnlyList<EventRow> rows, double asOf, DateOnly min, DateOnly max) {
        var kept = new Dictionary<(string Type, bool Ultra, DateOnly Date), Occurrence>();
        foreach (var row in rows.Where(r => r.Start < asOf).OrderBy(r => r.Start)) {
            if (!UnixSeconds.IsValid(row.Start) || !UnixSeconds.IsValid(row.End)) continue;
            var date = NoonEastern.LocalDate(row.Start);
            if (date < min || date > max) continue;
            kept.TryAdd(
                (row.Type, row.Ultra, date),
                new Occurrence(row.Type, row.Ultra, date, row.Start, Math.Max(row.End - row.Start, 0)));
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

    private static List<string> Types(List<Occurrence> occurrences) => [
        .. occurrences
            .Select(o => o.Type)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
    ];

    private static Dictionary<string, double> Weights(IEnumerable<Occurrence> occurrences) => occurrences
        .GroupBy(o => o.Type, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => (double)g.Count(), StringComparer.Ordinal);

    private static Dictionary<DayOfWeek, Dictionary<string, double>> WeightsByWeekday(
        List<Occurrence> occurrences) => occurrences
        .GroupBy(o => o.Date.DayOfWeek)
        .ToDictionary(g => g.Key, Weights);

    private static double Fill(int observed, int expected) => expected > 0 ? observed / (double)expected : 0;

    private static string LaneKey(FixedLane lane) => $"fixed:{(int)lane.Weekday}:{lane.Type}";

    private sealed record Occurrence(string Type, bool Ultra, DateOnly Date, double Start, double Duration);

    private sealed record FixedLane(
        string Type, DayOfWeek Weekday, int Period, DateOnly Anchor, HashSet<DateOnly> Grid,
        int Observed, int Expected, double Duration, double LastStart);

    private sealed record PoolSlot(
        DayOfWeek Weekday, int Observed, int Expected, double Duration, double LastStart);

    private sealed record UltraLane(
        int Period, DateOnly Anchor, int Observed, int Expected, double Duration, double LastStart);

    private sealed record Plan(double Start, DateOnly Date, FixedLane? Lane, PoolSlot? Slot, UltraLane? Ultra) {
        public int Rank => Lane is not null ? 0 : Slot is not null ? 1 : 2;
    }

    private sealed class History {
        public Dictionary<(string Type, bool Ultra), double> LastStart { get; } = [];
        public Dictionary<(string Type, bool Ultra), DateOnly> LastDate { get; } = [];
        public Dictionary<(string Type, bool Ultra), double> GapDays { get; } = [];
        public HashSet<(int Year, int Week, string Type, bool Ultra)> WeekUsed { get; } = [];
        public string? LastUltraType { get; set; }
    }
}
