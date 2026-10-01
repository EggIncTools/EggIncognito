using EggIncognito.Models.Events;

namespace EggIncognito.Services.Predictions.Events;

public sealed class UltraCadenceRule : IEventRule {
    public const int MinSamples = 4;
    public const double MaxJitterDays = 0.5;
    public const double MinFill = 0.8;
    public const int MinGapSamples = 2;
    private const double Smoothing = 0.5;
    private const double MinDue = 0.1;
    private const double MaxDue = 3d;

    private readonly DateOnly _anchor;
    private readonly double _duration;
    private readonly IReadOnlyDictionary<string, double> _prior;
    private readonly double _total;

    private UltraCadenceRule(
        DateOnly anchor, int period, double duration, IReadOnlyList<string> types,
        IReadOnlyDictionary<string, double> prior, double total, RuleEvidence evidence) {
        _anchor = anchor;
        PeriodDays = period;
        _duration = duration;
        Types = types;
        _prior = prior;
        _total = total;
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

    public static UltraCadenceRule? Fit(EventHistory history) {
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
        var evidence = new RuleEvidence(
            dates.Count, expected, ordered[^1].Start,
            $"Every {period} days, {dates.Count} of {expected}, {types.Count} types, each due by its own median gap");
        return new UltraCadenceRule(
            dates[^1], period, RobustStats.Median(ordered.Select(o => o.Duration).ToList()),
            types, prior, ordered.Count, evidence);
    }

    public IEnumerable<DateOnly> Dates(double asOf, double horizonEnd) =>
        RuleDates.Future(_anchor, PeriodDays, asOf, horizonEnd);

    public EventPrediction Realize(DateOnly date, EventLedger ledger) {
        double start = NoonEastern.SlotTime(date);
        var candidates = Score(ledger, start, date);
        var top = candidates.Count > 0 ? candidates[0] : null;
        if (top is not null) ledger.Guess(top.Type, true, date, top.Probability);
        return new EventPrediction(
            top?.Type, true, Kind, Key, Evidence.Summary, start, start + _duration, top?.Probability ?? 0,
            candidates, Evidence.Observed, Evidence.Expected, PeriodDays, Evidence.LastStart);
    }

    EventPrediction? IEventRule.Realize(DateOnly date, EventLedger ledger) => Realize(date, ledger);

    private List<EventCandidate> Score(EventLedger ledger, double at, DateOnly date) {
        var scores = new List<(string Type, double Score)>(Types.Count);
        double sum = 0;
        foreach (string type in Types) {
            double score = 0;
            if (!ledger.Repeats(type, true, date, PeriodDays)) {
                double prior = (_prior.GetValueOrDefault(type) + Smoothing) / (_total + Smoothing * Types.Count);
                score = prior * ledger.Due(type, true, at, MinDue, MaxDue, MinGapSamples)
                        * ledger.GuessedRepeatWeight(type, true, date, PeriodDays)
                        * ledger.GuessedDueWeight(type, true, at);
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
