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
            var evidence = new RuleEvidence(
                observed, expected, mine.Max(o => o.Start),
                $"{RuleDates.Weekday(weekday)} slot, {observed} of {expected} days, {seen} types seen, ranked by how overdue each type is");
            rules.Add(new RotatingPoolRule(
                weekday, types, gaps, RobustStats.Median(mine.Select(o => o.Duration).ToList()), evidence));
        }

        return rules;
    }

    public IEnumerable<DateOnly> Dates(double asOf, double horizonEnd) =>
        RuleDates.FutureDays(asOf, horizonEnd).Where(d => d.DayOfWeek == Weekday);

    public EventPrediction Realize(DateOnly date, EventLedger ledger) {
        double start = NoonEastern.SlotTime(date);
        var candidates = Score(ledger, start, date);
        var top = candidates.Count > 0 ? candidates[0] : null;
        if (top is not null) ledger.Guess(top.Type, false, date, top.Probability);
        return new EventPrediction(
            top?.Type, false, Kind, Key, Evidence.Summary, start, start + _duration, top?.Probability ?? 0,
            candidates, Evidence.Observed, Evidence.Expected, 1, Evidence.LastStart);
    }

    EventPrediction? IEventRule.Realize(DateOnly date, EventLedger ledger) => Realize(date, ledger);

    private List<EventCandidate> Score(EventLedger ledger, double at, DateOnly date) {
        var scores = new List<(string Type, double Score)>(Types.Count);
        double sum = 0;
        foreach (string type in Types) {
            double score = 0;
            if (!ledger.Repeats(type, false, date, RepeatBlockDays) && _gaps.TryGetValue(type, out var gaps)) {
                score = PoolHazard.Hazard(gaps, ledger.DaysSince(type, false, at))
                        * ledger.GuessedRepeatWeight(type, false, date, RepeatBlockDays)
                        * ledger.GuessedDueWeight(type, false, at)
                        * ledger.WeekWeight(date, type, false, SameWeekPenalty);
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
        ];
    }
}
