using EggIncognito.Models.Events;

namespace EggIncognito.Services.Predictions.Events;

public sealed class AlternatingLaneRule : IEventRule {
    public const int MinMemberSamples = 3;
    public const double MinMemberFill = 0.3;
    public const double MaxMemberFill = 0.7;
    public const double MinUnionFill = 0.8;
    public const double MinCycleConformance = 0.85;

    private readonly int _anchorIx;
    private readonly DateOnly _anchor;
    private readonly IReadOnlyDictionary<string, double> _durations;
    private readonly IReadOnlyDictionary<string, HashSet<DateOnly>> _memberDates;

    private AlternatingLaneRule(
        DayOfWeek weekday, IReadOnlyList<string> cycle, int anchorIx, DateOnly anchor,
        IReadOnlyDictionary<string, double> durations, IReadOnlyDictionary<string, HashSet<DateOnly>> memberDates,
        RuleEvidence evidence) {
        Weekday = weekday;
        Members = cycle;
        _anchorIx = anchorIx;
        _anchor = anchor;
        _durations = durations;
        _memberDates = memberDates;
        Evidence = evidence;
        Key = $"alt:{(int)weekday}:{string.Join('+', cycle)}";
    }

    public EventRuleKind Kind => EventRuleKind.Alternating;

    public string Key { get; }

    public string? Type => null;

    public bool Ultra => false;

    public int PeriodDays => WeekdayLaneRule.Period * Members.Count;

    public int Rank => 0;

    public DayOfWeek Weekday { get; }

    public IReadOnlyList<string> Members { get; }

    public RuleEvidence Evidence { get; }

    public IReadOnlySet<DateOnly> MemberDates(string type) =>
        _memberDates.TryGetValue(type, out var dates) ? dates : [];

    public string? TypeOn(DateOnly date) {
        if (date.DayOfWeek != Weekday) return null;
        int steps = (date.DayNumber - _anchor.DayNumber) / WeekdayLaneRule.Period;
        int n = Members.Count;
        return Members[((_anchorIx + steps) % n + n) % n];
    }

    public static List<AlternatingLaneRule> Fit(EventHistory history, HashSet<(string Type, DayOfWeek Weekday)> claimed) {
        var rules = new List<AlternatingLaneRule>();
        foreach (var weekday in Enum.GetValues<DayOfWeek>()) {
            int expected = history.WeekdayCount(weekday);
            if (expected < WeekdayLaneRule.MinSamples) continue;
            var members = history.Standard
                .Where(o => o.Date.DayOfWeek == weekday && !claimed.Contains((o.Type, weekday)))
                .GroupBy(o => o.Type, StringComparer.Ordinal)
                .Where(g => {
                    int n = g.Select(o => o.Date).Distinct().Count();
                    return n >= MinMemberSamples && n >= MinMemberFill * expected && n <= MaxMemberFill * expected;
                })
                .ToList();
            if (members.Count < 2) continue;

            var occurrences = members.SelectMany(g => g).OrderBy(o => o.Start).ToList();
            var dates = occurrences.Select(o => o.Date).ToList();
            if (dates.Distinct().Count() != dates.Count) continue;
            if (dates.Count < MinUnionFill * expected) continue;

            var sequence = occurrences.Select(o => o.Type).ToList();
            int n = members.Count;
            var cycle = sequence.TakeLast(n).ToList();
            if (cycle.Distinct(StringComparer.Ordinal).Count() != n) continue;
            if (Conformance(sequence, cycle) < MinCycleConformance) continue;

            var memberDates = members.ToDictionary(
                g => g.Key, g => g.Select(o => o.Date).ToHashSet(), StringComparer.Ordinal);
            var durations = members.ToDictionary(
                g => g.Key, g => RobustStats.Median(g.Select(o => o.Duration).ToList()), StringComparer.Ordinal);
            var evidence = new RuleEvidence(
                dates.Count, expected, occurrences[^1].Start,
                $"{RuleDates.Weekday(weekday)}, {string.Join(" and ", cycle)} alternate every {WeekdayLaneRule.Period * n} days, {dates.Count} of {expected}");
            rules.Add(new AlternatingLaneRule(weekday, cycle, n - 1, dates[^1], durations, memberDates, evidence));
            foreach (var member in cycle) claimed.Add((member, weekday));
        }

        return rules;
    }

    private static double Conformance(List<string> sequence, List<string> cycle) {
        int n = cycle.Count;
        int last = sequence.Count - 1;
        int matches = 0;
        for (int i = 0; i < sequence.Count; i++) {
            int ix = ((n - 1 - (last - i)) % n + n) % n;
            if (string.Equals(sequence[i], cycle[ix], StringComparison.Ordinal)) matches++;
        }

        return matches / (double)sequence.Count;
    }

    public IEnumerable<DateOnly> Dates(double asOf, double horizonEnd) =>
        RuleDates.Future(_anchor, WeekdayLaneRule.Period, asOf, horizonEnd);

    public EventPrediction? Realize(DateOnly date, EventLedger ledger) {
        if (TypeOn(date) is not { } type) return null;
        double start = NoonEastern.SlotTime(date);
        double duration = _durations.GetValueOrDefault(type, EventHistory.Day);
        ledger.Touch(type, false, start, date);
        return new EventPrediction(
            type, false, Kind, Key, Evidence.Summary, start, start + duration, Evidence.Fill,
            [new EventCandidate(type, 1)], Evidence.Observed, Evidence.Expected, PeriodDays, Evidence.LastStart);
    }
}
