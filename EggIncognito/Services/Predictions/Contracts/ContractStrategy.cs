namespace EggIncognito.Services.Predictions.Contracts;

public enum PeMembership { Shared, Alternating }

public sealed record ContractStrategy(
    PeMembership PeMembership,
    int RankWindowDays,
    double RankSmoothing,
    double RecencyGapMultiplier,
    bool SkipNeverRerun) : IStrategy {
    public string Label =>
        $"pe={PeMembership} window={RankWindowDays}d smoothing={RankSmoothing} gap={RecencyGapMultiplier}x skip={SkipNeverRerun}";

    public static readonly ContractStrategy Default = new(PeMembership.Shared, 365, 0.5, 2, false);

    public static readonly IReadOnlyList<ContractStrategy> Grid = Build();

    private static IReadOnlyList<ContractStrategy> Build() {
        PeMembership[] memberships = [PeMembership.Shared, PeMembership.Alternating];
        bool[] skips = [false, true];
        int[] windows = [180, 365];
        double[] smoothings = [0.1, 0.5];
        double[] gapMultipliers = [2, 3];

        var all = new List<ContractStrategy>();
        foreach (var membership in memberships)
            foreach (var skip in skips)
                foreach (var window in windows)
                    foreach (var smoothing in smoothings)
                        foreach (var gap in gapMultipliers)
                            all.Add(new ContractStrategy(membership, window, smoothing, gap, skip));

        return [.. all.OrderBy(s => s == Default ? 0 : 1)];
    }
}
