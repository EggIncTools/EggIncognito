namespace EggIncognito.Services.Predictions.Events;

public enum PoolScorer {
    Hazard,
    WeekdayPrior,
    DistanceHistogram
}

public sealed record EventStrategy(
    PoolScorer PoolScorer, double SameWeekPenalty, double UltraMinDue, double UltraMaxDue, double Smoothing) : IStrategy {
    public static readonly EventStrategy Default = new(PoolScorer.Hazard, 0.05, 0.1, 3, 0.5);

    public static readonly IReadOnlyList<EventStrategy> Grid = Build();

    public string Label =>
        FormattableString.Invariant($"{PoolScorer}/week={SameWeekPenalty}/due={UltraMinDue}-{UltraMaxDue}/smooth={Smoothing}");

    private static List<EventStrategy> Build() {
        var scorers = new[] { PoolScorer.Hazard, PoolScorer.WeekdayPrior, PoolScorer.DistanceHistogram };
        var penalties = new[] { 0.05, 0.25 };
        var due = new[] { (0.1, 3d), (0.3, 2d) };
        var smoothings = new[] { 0.5, 1d };
        var grid = new List<EventStrategy>();
        foreach (var scorer in scorers) {
            foreach (var penalty in penalties) {
                foreach (var (min, max) in due) {
                    foreach (var smoothing in smoothings) {
                        grid.Add(new EventStrategy(scorer, penalty, min, max, smoothing));
                    }
                }
            }
        }

        grid.Remove(Default);
        grid.Insert(0, Default);
        return grid;
    }
}
