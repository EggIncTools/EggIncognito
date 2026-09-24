using System.Collections.Frozen;

namespace EggIncognito.Services;

public static class ShipNameMap {
    public static readonly IReadOnlyList<Ship> All = [
        new(0, "ChickenOne", "Chicken One", "ei_ship_chicken_one", "afx_ship_chicken_1"),
        new(1, "ChickenNine", "Chicken Nine", "ei_ship_chicken_nine", "afx_ship_chicken_9"),
        new(2, "ChickenHeavy", "Chicken Heavy", "ei_ship_chicken_heavy", "afx_ship_chicken_heavy"),
        new(3, "Bcr", "BCR", "ei_ship_bcr", "afx_ship_bcr"),
        new(4, "MilleniumChicken", "Quintillion Chicken", "ei_ship_millenium_chicken", "afx_ship_millenium_chicken"),
        new(5, "CorellihenCorvette", "Cornish-Hen Corvette", "ei_ship_corellihen_corvette",
            "afx_ship_corellihen_corvette"),
        new(6, "Galeggtica", "Galeggtica", null, "afx_ship_galeggtica"),
        new(7, "Chickfiant", "Defihent", null, "afx_ship_defihent"),
        new(8, "Voyegger", "Voyegger", null, "afx_ship_voyegger"),
        new(9, "Henerprise", "Henerprise", null, "afx_ship_henerprise"),
        new(10, "Atreggies", "Atreggies Henliner", "ei_ship_atreggies_shuttle", "afx_ship_atreggies")
    ];

    private static readonly FrozenDictionary<string, string> StemToEnum =
        All.Where(s => s.BundleStem is not null)
            .ToFrozenDictionary(s => s.BundleStem!, s => s.EnumName, StringComparer.OrdinalIgnoreCase);

    public static string? EnumNameForStem(string stem) =>
        StemToEnum.GetValueOrDefault(stem);

    public static bool IsBundledShip(string stem) => StemToEnum.ContainsKey(stem);

    public sealed record Ship(int Tier, string EnumName, string Display, string? BundleStem, string? ShellAsset);
}
