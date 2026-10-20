using EggSharp.GameData;

namespace EggIncognito.Core.Services.Artifacts;

public static class CraftOdds {
    public const double MaxChance = 0.1;
    public const double FloorConstant = 10;
    public const double CountDomain = 400;
    public const double CountExponentDrop = 0.3;

    public static double[] Constants(IArtifactCatalog catalog, int afxId, int afxLevel) {
        var rows = catalog.Artifacts.Where(a => a.AfxId == afxId && a.AfxLevel == afxLevel).ToList();
        double common = rows.FirstOrDefault(a => a.AfxRarity == 0)?.OddsMultiplier ?? 0;
        double[] constants = new double[4];
        for (int r = 0; r < 4; r++) {
            double shiny = rows.FirstOrDefault(a => a.AfxRarity == r)?.OddsMultiplier ?? 0;
            constants[r] = common > 0 && shiny > 0 ? Math.Round(common / shiny, MidpointRounding.AwayFromZero) : 0;
        }

        return constants;
    }

    public static double Constant(IArtifactCatalog catalog, int afxId, int afxLevel, int rarity) => Constants(catalog, afxId, afxLevel)[rarity];

    public static double Threshold(double constant, double multiplier, uint count) {
        if (constant <= 0) return 0;
        double scaled = Math.Max(FloorConstant, constant / multiplier);
        return Math.Min(MaxChance, Math.Pow(1 / scaled, 1 - Math.Min(1, count / CountDomain) * CountExponentDrop));
    }

    public static double Threshold(IArtifactCatalog catalog, int afxId, int afxLevel, int rarity, double multiplier, uint count) =>
        Threshold(Constant(catalog, afxId, afxLevel, rarity), multiplier, count);

    public static double Chance(IReadOnlyList<double> constants, int rarity, double multiplier, uint count) {
        double above = 0;
        for (int higher = rarity + 1; higher <= 3; higher++)
            above = Math.Max(above, Threshold(constants[higher], multiplier, count));
        return Math.Max(0, Threshold(constants[rarity], multiplier, count) - above);
    }

    public static double Chance(IArtifactCatalog catalog, int afxId, int afxLevel, int rarity, double craftingXp, uint count) =>
        Chance(Constants(catalog, afxId, afxLevel), rarity, Multiplier(catalog, Level(catalog, craftingXp)), count);

    public static int Level(IArtifactCatalog catalog, double craftingXp) {
        var levels = Levels(catalog);
        int level = 1;
        double start = 0;
        for (int i = 0; i < levels.Count - 1; i++) {
            start += levels[i].XpRequired;
            if (craftingXp < start) break;
            level = i + 2;
        }

        return level;
    }

    public static double LevelStart(IArtifactCatalog catalog, int level) => Levels(catalog).Take(level - 1).Sum(l => l.XpRequired);

    public static double[] Multipliers(IArtifactCatalog catalog) => [.. Levels(catalog).Select(l => l.RarityMult)];

    public static double Multiplier(IArtifactCatalog catalog, int level) {
        var levels = Levels(catalog);
        return level >= 1 && level <= levels.Count ? levels[level - 1].RarityMult : 1;
    }

    private static List<ArtifactCraftingLevelEntry> Levels(IArtifactCatalog catalog) => [.. catalog.CraftingLevels.OrderBy(l => l.Level)];
}
