using EggSharp.GameData;

namespace EggIncognito.Core.Services.Artifacts;

public static class CraftOdds {
    public const double MaxChance = 0.1;
    public const double FloorConstant = 10;
    public const double CountDomain = 400;
    public const double CountExponentDrop = 0.3;

    public static double Constant(IArtifactCatalog catalog, int afxId, int afxLevel, int rarity) {
        var rows = catalog.Artifacts.Where(a => a.AfxId == afxId && a.AfxLevel == afxLevel).ToList();
        double common = rows.FirstOrDefault(a => a.AfxRarity == 0)?.OddsMultiplier ?? 0;
        double shiny = rows.FirstOrDefault(a => a.AfxRarity == rarity)?.OddsMultiplier ?? 0;
        return common > 0 && shiny > 0 ? Math.Round(common / shiny, MidpointRounding.AwayFromZero) : 0;
    }

    public static double Threshold(IArtifactCatalog catalog, int afxId, int afxLevel, int rarity, double multiplier, uint count) {
        double constant = Constant(catalog, afxId, afxLevel, rarity);
        if (constant <= 0) return 0;
        double scaled = Math.Max(FloorConstant, constant / multiplier);
        return Math.Min(MaxChance, Math.Pow(1 / scaled, 1 - Math.Min(1, count / CountDomain) * CountExponentDrop));
    }

    public static double ChanceAtLevel(IArtifactCatalog catalog, int afxId, int afxLevel, int rarity, int craftingLevel, uint count) {
        double multiplier = Multiplier(catalog, craftingLevel);
        double above = 0;
        for (int higher = rarity + 1; higher <= 3; higher++)
            above = Math.Max(above, Threshold(catalog, afxId, afxLevel, higher, multiplier, count));
        return Math.Max(0, Threshold(catalog, afxId, afxLevel, rarity, multiplier, count) - above);
    }

    public static double Chance(IArtifactCatalog catalog, int afxId, int afxLevel, int rarity, double craftingXp, uint count) =>
        ChanceAtLevel(catalog, afxId, afxLevel, rarity, Level(catalog, craftingXp), count);

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

    public static int MaxLevel(IArtifactCatalog catalog) => Math.Max(1, catalog.CraftingLevels.Count);

    public static double LevelStart(IArtifactCatalog catalog, int level) => Levels(catalog).Take(level - 1).Sum(l => l.XpRequired);

    public static double Multiplier(IArtifactCatalog catalog, int level) {
        var levels = Levels(catalog);
        return level >= 1 && level <= levels.Count ? levels[level - 1].RarityMult : 1;
    }

    private static List<ArtifactCraftingLevelEntry> Levels(IArtifactCatalog catalog) => [.. catalog.CraftingLevels.OrderBy(l => l.Level)];
}
