using EggIncognito.Models.Events;

namespace EggIncognito.Services.Predictions.Events;

public sealed class MagnitudeModel {
    public const int WindowDays = 365;

    private readonly double _common;
    private readonly IReadOnlyList<RareChain> _rare;

    private sealed class RareChain(double multiplier, double share, IReadOnlyList<int> gaps, int since) {
        private double[] _state = Point(since);

        public double Multiplier { get; } = multiplier;

        private static double[] Point(int n) {
            var state = new double[n + 1];
            state[n] = 1;
            return state;
        }

        private double Hazard(int n) {
            if (n <= 0) return 0;
            int atOrAfter = 0, exact = 0;
            foreach (int g in gaps) {
                if (g >= n) atOrAfter++;
                if (g == n) exact++;
            }

            return atOrAfter == 0 ? share : (exact + share) / (atOrAfter + 1);
        }

        public double Odds() {
            double p = 0;
            for (int n = 0; n < _state.Length; n++) p += _state[n] * Hazard(n + 1);
            return p;
        }

        public void Advance(double occurs) {
            var next = new double[_state.Length + 1];
            for (int n = 0; n < _state.Length; n++) {
                double h = Hazard(n + 1);
                next[n] += _state[n] * (1 - occurs);
                next[0] += _state[n] * occurs * h;
                next[n + 1] += _state[n] * occurs * (1 - h);
            }

            _state = next;
        }
    }

    private MagnitudeModel(double common, IReadOnlyList<RareChain> rare) {
        _common = common;
        _rare = rare;
    }

    public static MagnitudeModel Build(IEnumerable<EventRow> runs, double asOf) {
        double windowStart = asOf - WindowDays * EventHistory.Day;
        var sequence = runs
            .Where(r => r.Start < asOf && r.Start >= windowStart && r.Multiplier > 0)
            .GroupBy(r => NoonEastern.LocalDate(r.Start))
            .OrderBy(g => g.Key)
            .Select(g => g.First().Multiplier)
            .ToList();
        if (sequence.Count == 0) return new MagnitudeModel(0, []);
        var counts = sequence.GroupBy(m => m).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
        var rare = new List<RareChain>();
        foreach (var group in counts.Skip(1)) {
            var at = sequence.Select((m, i) => (m, i)).Where(x => x.m == group.Key).Select(x => x.i).ToList();
            var gaps = at.Zip(at.Skip(1), (a, b) => b - a).ToList();
            rare.Add(new RareChain(group.Key, group.Count() / (double)sequence.Count, gaps, sequence.Count - 1 - at[^1]));
        }

        return new MagnitudeModel(counts[0].Key, rare);
    }

    public IReadOnlyList<EventMagnitudeOdds> Step(double occurs) {
        if (_common == 0) return [];
        var odds = _rare.Select(c => (c.Multiplier, P: c.Odds())).ToList();
        double rareSum = odds.Sum(o => o.P);
        if (rareSum > 1) odds = [.. odds.Select(o => (o.Multiplier, o.P / rareSum))];
        foreach (var chain in _rare) chain.Advance(occurs);
        return [
            .. odds.Prepend((_common, Math.Max(0, 1 - Math.Min(1, rareSum))))
                .Select(o => new EventMagnitudeOdds(o.Item1, o.Item2))
                .OrderByDescending(o => o.Probability)
        ];
    }
}
