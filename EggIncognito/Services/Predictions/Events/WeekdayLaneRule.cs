using EggIncognito.Models.Events;

namespace EggIncognito.Services.Predictions.Events;

public sealed class WeekdayLaneRule : IEventRule {
    public const int Period = 7;
    public const int MinSamples = 4;
    public const double MinFill = 0.8;
    private const int MinTrailingRun = 13;

    private readonly DateOnly _anchor;
    private readonly double _duration;

    private WeekdayLaneRule(
        string type, DayOfWeek weekday, DateOnly anchor, HashSet<DateOnly> observed, double duration, RuleEvidence evidence) {
        Type = type;
        Weekday = weekday;
        _anchor = anchor;
        Observed = observed;
        _duration = duration;
        Evidence = evidence;
        Key = $"lane:{(int)weekday}:{type}";
    }

    public EventRuleKind Kind => EventRuleKind.WeekdayLane;

    public string Key { get; }

    public string? Type { get; }

    public bool Ultra => false;

    public int PeriodDays => Period;

    public int Rank => 0;

    public DayOfWeek Weekday { get; }

    public IReadOnlySet<DateOnly> Observed { get; }

    public RuleEvidence Evidence { get; }

    public static List<WeekdayLaneRule> Fit(EventHistory history) {
        var lanes = new List<WeekdayLaneRule>();
        foreach (var group in history.Standard.GroupBy(o => (o.Type, o.Date.DayOfWeek))) {
            var occurrences = group.OrderBy(o => o.Start).ToList();
            int expected = history.WeekdayCount(group.Key.DayOfWeek);
            if (expected < MinSamples) continue;
            var dates = occurrences.Select(o => o.Date).ToHashSet();
            var anchor = occurrences[^1].Date;
            if (dates.Count < MinFill * expected && !HeldRecently(anchor, dates, history.Last)) continue;
            var evidence = new RuleEvidence(
                dates.Count, expected, occurrences[^1].Start,
                $"{RuleDates.Weekday(group.Key.DayOfWeek)}, every {Period} days, {dates.Count} of {expected}");
            lanes.Add(new WeekdayLaneRule(
                group.Key.Type, group.Key.DayOfWeek, anchor, dates,
                RobustStats.Median(occurrences.Select(o => o.Duration).ToList()), evidence));
        }

        return lanes;
    }

    private static bool HeldRecently(DateOnly anchor, HashSet<DateOnly> dates, DateOnly last) {
        if (anchor.AddDays(Period) <= last) return false;
        int run = 0;
        for (var d = anchor; dates.Contains(d); d = d.AddDays(-Period)) run++;
        return run >= MinTrailingRun;
    }

    public IEnumerable<DateOnly> Dates(double asOf, double horizonEnd) =>
        RuleDates.Future(_anchor, Period, asOf, horizonEnd);

    public EventPrediction Realize(DateOnly date, EventLedger ledger) {
        double start = NoonEastern.SlotTime(date);
        ledger.Touch(Type!, false, start, date);
        return new EventPrediction(
            Type, false, Kind, Key, Evidence.Summary, start, start + _duration, Evidence.Fill,
            [new EventCandidate(Type!, 1)], Evidence.Observed, Evidence.Expected, Period, Evidence.LastStart);
    }

    EventPrediction? IEventRule.Realize(DateOnly date, EventLedger ledger) => Realize(date, ledger);
}
