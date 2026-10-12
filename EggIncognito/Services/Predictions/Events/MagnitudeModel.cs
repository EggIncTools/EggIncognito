using EggIncognito.Models.Events;

namespace EggIncognito.Services.Predictions.Events;

public sealed class MagnitudeModel {
    public const int WindowDays = 365;
    public const int MinCycleRuns = 3;
    public const int SlackDays = 1;

    private readonly IReadOnlyList<(double Multiplier, double Share)> _shares;
    private readonly IReadOnlyList<Cycle> _cycles;

    private sealed record Cycle(double Multiplier, int PeriodDays, DateOnly Last, double Fit);

    private MagnitudeModel(IReadOnlyList<(double, double)> shares, IReadOnlyList<Cycle> cycles) {
        _shares = shares;
        _cycles = cycles;
    }

    public static MagnitudeModel Build(IEnumerable<EventRow> runs, double asOf) {
        double windowStart = asOf - WindowDays * EventHistory.Day;
        var recent = runs
            .Where(r => r.Start < asOf && r.Start >= windowStart && r.Multiplier > 0)
            .GroupBy(r => (Date: NoonEastern.LocalDate(r.Start), r.Multiplier))
            .Select(g => g.Key)
            .OrderBy(k => k.Date)
            .ToList();
        if (recent.Count == 0) return new MagnitudeModel([], []);
        var shares = recent
            .GroupBy(k => k.Multiplier)
            .Select(g => (g.Key, (double)g.Count() / recent.Count))
            .OrderByDescending(s => s.Item2)
            .ToList();
        var cycles = new List<Cycle>();
        if (shares.Count > 1) {
            foreach (var (multiplier, _) in shares) {
                var dates = recent.Where(k => k.Multiplier == multiplier).Select(k => k.Date).ToList();
                if (CycleOf(multiplier, dates) is { } cycle) cycles.Add(cycle);
            }
        }

        return new MagnitudeModel(shares, cycles);
    }

    private static Cycle? CycleOf(double multiplier, IReadOnlyList<DateOnly> dates) {
        if (dates.Count < MinCycleRuns) return null;
        var gaps = dates.Zip(dates.Skip(1), (a, b) => (double)(b.DayNumber - a.DayNumber)).ToList();
        double median = RobustStats.Median(gaps);
        int period = (int)Math.Round(median);
        if (period < 2 || RobustStats.Mad(gaps, median) > Math.Max(SlackDays, period * 0.1)) return null;
        var first = dates[0];
        var last = dates[^1];
        int expected = (int)Math.Round((last.DayNumber - first.DayNumber) / (double)period) + 1;
        int hits = dates.Count(d => OnCycle(d, last, period));
        return new Cycle(multiplier, period, last, (Math.Min(hits, expected) + 1d) / (expected + 2d));
    }

    private static bool OnCycle(DateOnly date, DateOnly anchor, int period) {
        int offset = ((date.DayNumber - anchor.DayNumber) % period + period) % period;
        return offset <= SlackDays || offset >= period - SlackDays;
    }

    public IReadOnlyList<EventMagnitudeOdds> At(double start) {
        if (_shares.Count == 0) return [];
        if (_shares.Count == 1) return [new EventMagnitudeOdds(_shares[0].Multiplier, 1)];
        var date = NoonEastern.LocalDate(start);
        var claim = _cycles.Where(c => date > c.Last && OnCycle(date, c.Last, c.PeriodDays)).MaxBy(c => c.Fit);
        var odds = new Dictionary<double, double>();
        double rest = 1;
        if (claim is not null) {
            odds[claim.Multiplier] = claim.Fit;
            rest = 1 - claim.Fit;
        }

        var weights = _shares
            .Where(s => s.Multiplier != claim?.Multiplier)
            .Select(s => (s.Multiplier, Weight: _cycles.FirstOrDefault(c => c.Multiplier == s.Multiplier) is { } cycle
                ? s.Share * (1 - cycle.Fit)
                : s.Share))
            .ToList();
        double weightSum = weights.Sum(w => w.Weight);
        foreach (var (multiplier, weight) in weights) {
            odds[multiplier] = weightSum > 0 ? rest * weight / weightSum : 0;
        }

        return [.. odds.Select(o => new EventMagnitudeOdds(o.Key, o.Value)).OrderByDescending(o => o.Probability)];
    }
}
