using EggIncognito.Models.Events;

namespace EggIncognito.Services.Predictions.Events;

public sealed class PeriodicLaneRule : IEventRule {
    public const int MinSamples = 3;
    public const int MaxDistinctGaps = 2;
    public const int MinGap = 14;
    public const int MaxGap = 56;
    public const double MinWeekdayShare = 0.9;

    private readonly DateOnly _anchor;
    private readonly double _duration;
    private readonly Func<DateOnly, bool>? _hostOn;

    private PeriodicLaneRule(
        string type, DayOfWeek weekday, DateOnly anchor, int period, double duration, string? ridesOn,
        Func<DateOnly, bool>? hostOn, RuleEvidence evidence) {
        Type = type;
        Weekday = weekday;
        _anchor = anchor;
        PeriodDays = period;
        _duration = duration;
        RidesOn = ridesOn;
        _hostOn = hostOn;
        Evidence = evidence;
        Key = $"period:{(int)weekday}:{type}";
    }

    public EventRuleKind Kind => EventRuleKind.Periodic;

    public string Key { get; }

    public string? Type { get; }

    public bool Ultra => false;

    public int PeriodDays { get; }

    public int Rank => 1;

    public DayOfWeek Weekday { get; }

    public string? RidesOn { get; }

    public RuleEvidence Evidence { get; }

    public static List<PeriodicLaneRule> Fit(
        EventHistory history, HashSet<(string Type, DayOfWeek Weekday)> claimed,
        IReadOnlyList<AlternatingLaneRule> alternations) {
        var rules = new List<PeriodicLaneRule>();
        var unclaimed = history.Standard.Where(o => !claimed.Contains((o.Type, o.Date.DayOfWeek))).ToList();
        var perType = unclaimed.GroupBy(o => o.Type, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var group in unclaimed.GroupBy(o => (o.Type, o.Date.DayOfWeek))) {
            var occurrences = group.OrderBy(o => o.Start).ToList();
            if (occurrences.Count < MinWeekdayShare * perType[group.Key.Type]) continue;
            var dates = occurrences.Select(o => o.Date).Distinct().Order().ToList();
            if (dates.Count < MinSamples) continue;
            var gaps = new List<int>(dates.Count - 1);
            for (int i = 1; i < dates.Count; i++) gaps.Add(dates[i].DayNumber - dates[i - 1].DayNumber);
            if (gaps.Exists(g => g % WeekdayLaneRule.Period != 0 || g < MinGap || g > MaxGap)) continue;
            var distinct = gaps.Distinct().ToList();
            if (distinct.Count > MaxDistinctGaps || Switches(gaps) > 1) continue;

            int period = gaps[^1];
            double meanGap = gaps.Average();
            int expected = (int)Math.Round((history.Last.DayNumber - history.First.DayNumber) / meanGap);
            var host = alternations.FirstOrDefault(a => a.Weekday == group.Key.DayOfWeek
                                                        && a.Members.Any(m => a.MemberDates(m).IsSupersetOf(dates)));
            string? ridesOn = host?.Members.First(m => host.MemberDates(m).IsSupersetOf(dates));
            Func<DateOnly, bool>? hostOn = host is null || ridesOn is null
                ? null
                : d => string.Equals(host.TypeOn(d), ridesOn, StringComparison.Ordinal);
            string before = distinct.Count > 1 ? $" ({distinct.First(g => g != period)} before)" : "";
            string riding = ridesOn is null ? "" : $", on {ridesOn} {RuleDates.Weekday(group.Key.DayOfWeek)}s";
            var evidence = new RuleEvidence(
                dates.Count, Math.Max(expected, dates.Count), occurrences[^1].Start,
                $"{RuleDates.Weekday(group.Key.DayOfWeek)}, every {period} days{before}{riding}, {dates.Count} observed");
            rules.Add(new PeriodicLaneRule(
                group.Key.Type, group.Key.DayOfWeek, dates[^1], period,
                RobustStats.Median(occurrences.Select(o => o.Duration).ToList()), ridesOn, hostOn, evidence));
            claimed.Add((group.Key.Type, group.Key.DayOfWeek));
        }

        return rules;
    }

    private static int Switches(List<int> gaps) {
        int switches = 0;
        for (int i = 1; i < gaps.Count; i++) {
            if (gaps[i] != gaps[i - 1]) switches++;
        }

        return switches;
    }

    public IEnumerable<DateOnly> Dates(double asOf, double horizonEnd) {
        var date = _anchor.AddDays(PeriodDays);
        while (NoonEastern.SlotTime(date) < horizonEnd) {
            if (_hostOn is not null && !_hostOn(date)) {
                date = date.AddDays(WeekdayLaneRule.Period);
                continue;
            }

            if (NoonEastern.SlotTime(date) >= asOf) yield return date;
            date = date.AddDays(PeriodDays);
        }
    }

    public EventPrediction Realize(DateOnly date, EventLedger ledger) {
        double start = NoonEastern.SlotTime(date);
        ledger.Touch(Type!, false, start, date);
        return new EventPrediction(
            Type, false, Kind, Key, Evidence.Summary, start, start + _duration, Evidence.Fill,
            [new EventCandidate(Type!, 1)], Evidence.Observed, Evidence.Expected, PeriodDays, Evidence.LastStart);
    }

    EventPrediction? IEventRule.Realize(DateOnly date, EventLedger ledger) => Realize(date, ledger);
}
