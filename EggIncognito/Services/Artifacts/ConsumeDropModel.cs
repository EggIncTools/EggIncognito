using EggSharp.GameData;

namespace EggIncognito.Services.Artifacts;

public sealed record ConsumeDropItem(string Family, string Level, int AfxLevel, bool IsFragment, double Quality, double OddsMultiplier, double FamilyFactor, double Cost, double Weight);

public sealed record ConsumeDropPrediction(string Family, string Level, int AfxLevel, bool IsFragment, double Chance, double Mean);

public sealed record ConsumeDropExplanation(
    double InputQuality,
    int Rarity,
    double Budget,
    double WeightFloor,
    IReadOnlyList<ConsumeDropItem> Items,
    IReadOnlyList<ConsumeDropPrediction> Predictions) {
    public bool HasSecondFamily => Items.Any(i => i.FamilyFactor != 1);
}

public static class ConsumeDropModel {
    public const string Source = "Fitted to PROD consumes, 2026-10-20";

    public static readonly double[] BudgetPerQuality = [1.772, 3.537, 6.382, 7.976];
    public const double BudgetOffset = 0.202;
    public static readonly double[] FloorFraction = [0.119, 0.405, 0.430, 0.497];
    public static readonly double[] StoneWeight = [1.051, 3.192, 4.301, 5.416];
    public const double StoneCostPerQuality = 3.543;
    public static readonly double[] StoneCostOffset = [0.230, 0.286, 0.201, 0.185];
    public const double FragmentCostOffset = 0.177;
    public const int WeightExponent = 2;

    public static readonly IReadOnlyDictionary<string, (string Family, double Fragment, double Stone)> SecondFamily =
        new Dictionary<string, (string, double, double)>(StringComparer.Ordinal) {
            ["BEAK_OF_MIDAS"] = ("SHELL_STONE", 24.3, 25.0),
            ["DEMETERS_NECKLACE"] = ("SHELL_STONE", 25.7, 25.9),
            ["BOOK_OF_BASAN"] = ("SHELL_STONE", 3.06, 2.99),
            ["VIAL_MARTIAN_DUST"] = ("TERRA_STONE", 3.85, 3.86),
            ["ORNATE_GUSSET"] = ("TERRA_STONE", 1.84, 1.99),
            ["CARVED_RAINSTICK"] = ("TERRA_STONE", 1.17, 1.04),
            ["NEODYMIUM_MEDALLION"] = ("QUANTUM_STONE", 1.17, 1.05),
            ["PUZZLE_CUBE"] = ("SHELL_STONE", 0.84, 0.89),
            ["LIGHT_OF_EGGENDIL"] = ("PROPHECY_STONE", 0.18, 0.11),
            ["LUNAR_TOTEM"] = ("TERRA_STONE", 0.26, 0.11)
        };

    private const string FragmentSuffix = "_FRAGMENT";
    private const int MaxBundles = 20000;

    public static double Budget(double inputQuality, int rarity) => BudgetPerQuality[rarity] * inputQuality + BudgetOffset;

    public static double Floor(double inputQuality, int rarity) => FloorFraction[rarity] * inputQuality;

    public static double Cost(double quality, bool isFragment, int rarity) =>
        isFragment ? quality + FragmentCostOffset : StoneCostPerQuality * quality + StoneCostOffset[rarity];

    public static double Weight(double quality, double oddsMultiplier, bool isFragment, double floor, int rarity, double familyFactor) {
        double w = oddsMultiplier / Math.Pow(Math.Max(quality, floor), WeightExponent);
        if (!isFragment) w *= StoneWeight[rarity];
        return w * familyFactor;
    }

    public static ConsumeDropExplanation? Explain(IArtifactCatalog catalog, string artifact, int afxLevel, int rarity, IReadOnlyList<string> stoneFamilies) {
        var input = catalog.Artifacts.FirstOrDefault(a => a.SpecName == artifact && a.AfxLevel == afxLevel && a.AfxRarity == rarity)
                    ?? catalog.Artifacts.FirstOrDefault(a => a.SpecName == artifact && a.AfxLevel == afxLevel && a.AfxRarity == 0);
        if (input is null || stoneFamilies.Count == 0) return null;
        double qin = input.BaseQuality;
        double floor = Floor(qin, rarity);
        var items = new List<ConsumeDropItem>();
        foreach (string family in stoneFamilies.OrderBy(f => f, StringComparer.Ordinal)) {
            double fragFactor = 1, stoneFactor = 1;
            if (SecondFamily.TryGetValue(artifact, out var second) && second.Family == family) (fragFactor, stoneFactor) = (second.Fragment, second.Stone);
            var fragment = catalog.Artifacts.FirstOrDefault(a => a.SpecName == family + FragmentSuffix && a.AfxLevel == 0 && a.AfxRarity == 0);
            if (fragment is not null) {
                items.Add(new ConsumeDropItem(fragment.SpecName, fragment.Level, 0, true, fragment.BaseQuality, fragment.OddsMultiplier, fragFactor,
                    Cost(fragment.BaseQuality, true, rarity), Weight(fragment.BaseQuality, fragment.OddsMultiplier, true, floor, rarity, fragFactor)));
            }

            foreach (var stone in catalog.Artifacts.Where(a => a.SpecName == family && a.AfxRarity == 0).OrderBy(a => a.AfxLevel)) {
                items.Add(new ConsumeDropItem(stone.SpecName, stone.Level, stone.AfxLevel, false, stone.BaseQuality, stone.OddsMultiplier, stoneFactor,
                    Cost(stone.BaseQuality, false, rarity), Weight(stone.BaseQuality, stone.OddsMultiplier, false, floor, rarity, stoneFactor)));
            }
        }

        double budget = Budget(qin, rarity);
        return new ConsumeDropExplanation(qin, rarity, budget, floor, items, Predict(items, budget));
    }

    public static IReadOnlyList<ConsumeDropPrediction> Predict(IReadOnlyList<ConsumeDropItem> items, double budget) {
        int n = items.Count;
        double[] chance = new double[n];
        double[] mean = new double[n];
        var level = new Dictionary<int[], (double P, double Spent)>(new CountsComparer()) { [new int[n]] = (1.0, 0.0) };
        int visited = 0;
        while (level.Count > 0 && visited < MaxBundles) {
            var next = new Dictionary<int[], (double P, double Spent)>(new CountsComparer());
            foreach (var (counts, (p, spent)) in level) {
                visited++;
                double remaining = budget - spent;
                double total = 0;
                for (int i = 0; i < n; i++) if (items[i].Cost <= remaining + 1e-9) total += items[i].Weight;
                if (total <= 0) {
                    for (int i = 0; i < n; i++) {
                        if (counts[i] > 0) chance[i] += p;
                        mean[i] += p * counts[i];
                    }

                    continue;
                }

                for (int i = 0; i < n; i++) {
                    if (items[i].Cost > remaining + 1e-9) continue;
                    double pp = p * items[i].Weight / total;
                    int[] child = (int[])counts.Clone();
                    child[i]++;
                    next[child] = next.TryGetValue(child, out var existing) ? (existing.P + pp, existing.Spent) : (pp, spent + items[i].Cost);
                }
            }

            level = next;
        }

        return [.. items.Select((it, i) => new ConsumeDropPrediction(it.Family, it.Level, it.AfxLevel, it.IsFragment, chance[i], mean[i]))];
    }

    private sealed class CountsComparer : IEqualityComparer<int[]> {
        public bool Equals(int[]? a, int[]? b) => a is not null && b is not null && a.AsSpan().SequenceEqual(b);

        public int GetHashCode(int[] a) {
            var h = new HashCode();
            foreach (int v in a) h.Add(v);
            return h.ToHashCode();
        }
    }
}
