using EggIncognito.Models.Events;

namespace EggIncognito.Services.Predictions.Events;

public sealed class RotatingPoolRule : IEventRule {
    public const int MinSamples = 4;
    public const double MinFill = 0.8;
    public const int RepeatBlockDays = 1;
    private const double SameWeekPenalty = 0.05;

    private readonly IReadOnlyDictionary<string, IReadOnlyList<double>> _gaps;
    private readonly double _duration;

    private RotatingPoolRule(
        DayOfWeek weekday, IReadOnlyList<string> types, IReadOnlyDictionary<string, IReadOnlyList<double>> gaps,
        double duration, RuleEvidence evidence) {
        Weekday = weekday;
        Types = types;
        _gaps = gaps;
        _duration = duration;
        Evidence = evidence;
        Key = $"pool:{(int)weekday}";
    }

    public EventRuleKind Kind => EventRuleKind.Pool;

    public string Key { get; }

    public string? Type => null;

    public bool Ultra => false;

    public int PeriodDays => 1;

    public int Rank => 2;

    public DayOfWeek Weekday { get; }

    public IReadOnlyList<string> Types { get; }

    public RuleEvidence Evidence { get; }

    public static List<RotatingPoolRule> Fit(EventHistory history, HashSet<(string Type, DayOfWeek Weekday)> claimed) {
        var unclaimed = history.Standard.Where(o => !claimed.Contains((o.Type, o.Date.DayOfWeek))).ToList();
        var types = unclaimed.Select(o => o.Type).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var gaps = PoolHazard.GapsByType(unclaimed);
        var rules = new List<RotatingPoolRule>();
        foreach (var weekday in Enum.GetValues<DayOfWeek>()) {
            int expected = history.WeekdayCount(weekday);
            if (expected < MinSamples) continue;
            var mine = unclaimed.Where(o => o.Date.DayOfWeek == weekday).ToList();
            int observed = mine.Select(o => o.Date).Distinct().Count();
            if (observed < MinFill * expected) continue;
            int seen = mine.Select(o => o.Type).Distinct(StringComparer.Ordinal).Count();
            var filled = mine.Select(o => o.Date).ToHashSet();
            var evidence = new RuleEvidence(
                observed, expected, mine.Max(o => o.Start),
                $"{RuleDates.Weekday(weekday)} slot, {observed} of {expected} days, {seen} types seen, each type's odds from its own gap history",
                WeekdayLaneRule.HeldRecently(filled.Max(), filled, history.Last));
            rules.Add(new RotatingPoolRule(
                weekday, types, gaps, RobustStats.Median(mine.Select(o => o.Duration).ToList()), evidence));
        }

        return rules;
    }

    public IEnumerable<DateOnly> Dates(double asOf, double horizonEnd) =>
        RuleDates.FutureDays(asOf, horizonEnd).Where(d => d.DayOfWeek == Weekday);

    public double Duration(string type) => _duration;

    public IReadOnlyList<EventCandidate> Odds(DateOnly date, EventLedger ledger) {
        double at = NoonEastern.SlotTime(date);
        var scores = new List<(string Type, double Score)>(Types.Count);
        double sum = 0;
        foreach (string type in Types) {
            if (ledger.Repeats(type, false, date, RepeatBlockDays) || !_gaps.TryGetValue(type, out var gaps)) continue;
            double score = PoolHazard.Hazard(gaps, ledger.DaysSince(type, false, at))
                           * ledger.WeekWeight(date, type, false, SameWeekPenalty);
            scores.Add((type, score));
            sum += score;
        }

        double fill = Evidence.Confidence;
        return sum <= 0 ? [] : [.. scores.Where(s => s.Score > 0).Select(s => new EventCandidate(s.Type, fill * s.Score / sum))];
    }
}
