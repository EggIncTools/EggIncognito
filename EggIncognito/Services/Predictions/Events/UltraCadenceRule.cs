using EggIncognito.Models.Events;

namespace EggIncognito.Services.Predictions.Events;

public sealed class UltraCadenceRule : IEventRule {
    public const int MinSamples = 4;
    public const double MaxJitterDays = 0.5;
    public const double MinFill = 0.8;
    public const int MinGapSamples = 2;
    public const int MinTrailingRun = 13;

    private readonly DateOnly _anchor;
    private readonly double _duration;
    private readonly IReadOnlyDictionary<string, double> _prior;
    private readonly double _total;
    private readonly EventStrategy _strategy;

    private UltraCadenceRule(
        DateOnly anchor, int period, double duration, IReadOnlyList<string> types,
        IReadOnlyDictionary<string, double> prior, double total, EventStrategy strategy, RuleEvidence evidence) {
        _anchor = anchor;
        PeriodDays = period;
        _duration = duration;
        Types = types;
        _prior = prior;
        _total = total;
        _strategy = strategy;
        Evidence = evidence;
    }

    public EventRuleKind Kind => EventRuleKind.Ultra;

    public string Key => "ultra";

    public string? Type => null;

    public bool Ultra => true;

    public int PeriodDays { get; }

    public int Rank => 3;

    public IReadOnlyList<string> Types { get; }

    public RuleEvidence Evidence { get; }

    public static UltraCadenceRule? Fit(EventHistory history, EventStrategy strategy) {
        var ordered = history.Ultra.OrderBy(o => o.Start).ToList();
        var dates = ordered.Select(o => o.Date).Distinct().Order().ToList();
        if (dates.Count < MinSamples) return null;

        var intervals = new List<double>(dates.Count - 1);
        for (int i = 1; i < dates.Count; i++) intervals.Add(dates[i].DayNumber - dates[i - 1].DayNumber);
        double median = RobustStats.Median(intervals);
        if (median <= 0 || RobustStats.Mad(intervals, median) > MaxJitterDays) return null;

        int period = Math.Max((int)Math.Round(median), 1);
        int expected = (int)Math.Round(history.Days.Count / (double)period);
        if (expected < MinSamples || dates.Count < MinFill * expected) return null;

        var prior = ordered.GroupBy(o => o.Type, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (double)g.Count(), StringComparer.Ordinal);
        var types = prior.Keys.Order(StringComparer.Ordinal).ToList();
        int run = 1;
        while (run < dates.Count && dates[^run].DayNumber - dates[^(run + 1)].DayNumber == period) run++;
        bool holding = run >= MinTrailingRun && dates[^1].AddDays(period) > history.Last;
        var evidence = new RuleEvidence(
            dates.Count, expected, ordered[^1].Start,
            $"Every {period} days, {dates.Count} of {expected}, {types.Count} types, each due by its own median gap",
            holding);
        return new UltraCadenceRule(
            dates[^1], period, RobustStats.Median(ordered.Select(o => o.Duration).ToList()),
            types, prior, ordered.Count, strategy, evidence);
    }

    public IEnumerable<DateOnly> Dates(double asOf, double horizonEnd) =>
        RuleDates.Future(_anchor, PeriodDays, asOf, horizonEnd);

    public double Duration(string type) => _duration;

    public IReadOnlyList<EventCandidate> Odds(DateOnly date, EventLedger ledger) {
        double at = NoonEastern.SlotTime(date);
        var scores = new List<(string Type, double Score)>(Types.Count);
        double sum = 0;
        foreach (string type in Types) {
            if (ledger.Repeats(type, true, date, PeriodDays)) continue;
            double prior = (_prior.GetValueOrDefault(type) + _strategy.Smoothing) / (_total + _strategy.Smoothing * Types.Count);
            double score = prior * ledger.Due(type, true, at, _strategy.UltraMinDue, _strategy.UltraMaxDue, MinGapSamples);
            scores.Add((type, score));
            sum += score;
        }

        double fill = Evidence.Confidence;
        return sum <= 0 ? [] : [.. scores.Where(s => s.Score > 0).Select(s => new EventCandidate(s.Type, fill * s.Score / sum))];
    }
}
