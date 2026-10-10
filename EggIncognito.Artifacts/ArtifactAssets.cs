namespace EggIncognito.Artifacts;

public static class ArtifactAssets {
    public const string Base = "_content/EggIncognito.Artifacts/images/";

    public static string KeyFor(string name) => name switch {
        "ORNATE_GUSSET" => "GUSSET",
        "VIAL_MARTIAN_DUST" => "VIAL_OF_MARTIAN_DUST",
        _ => name
    };

    public static string IconPath(string key, int n) => $"{Base}artifacts/{key}/{key}_{n}.png";

    public static string TargetIconPath(string imageString) => $"{Base}targets/{imageString}";

    public static string TargetIconFor(string name) => TargetIconPath(TargetStem(name) + "_target.png");

    private static string TargetStem(string name) => name switch {
        "LUNAR_TOTEM" => "totem",
        "NEODYMIUM_MEDALLION" => "medallion",
        "BEAK_OF_MIDAS" => "beak",
        "LIGHT_OF_EGGENDIL" => "loe",
        "DEMETERS_NECKLACE" => "necklace",
        "VIAL_MARTIAN_DUST" => "vial",
        "ORNATE_GUSSET" => "gusset",
        "THE_CHALICE" => "chalice",
        "BOOK_OF_BASAN" => "bob",
        "PHOENIX_FEATHER" => "feather",
        "TUNGSTEN_ANKH" => "ankh",
        "AURELIAN_BROOCH" => "brooch",
        "CARVED_RAINSTICK" => "rainstick",
        "PUZZLE_CUBE" => "cube",
        "QUANTUM_METRONOME" => "metronome",
        "SHIP_IN_A_BOTTLE" => "siab",
        "TACHYON_DEFLECTOR" => "deflector",
        "INTERSTELLAR_COMPASS" => "compass",
        "DILITHIUM_MONOCLE" => "monocle",
        "TITANIUM_ACTUATOR" => "actuator",
        "MERCURYS_LENS" => "lens",
        "GOLD_METEORITE" => "gold",
        "TAU_CETI_GEODE" => "geode",
        "SOLAR_TITANIUM" => "titanium",
        _ when name.EndsWith("_STONE_FRAGMENT", StringComparison.Ordinal) => name[..^"_STONE_FRAGMENT".Length].ToLowerInvariant() + "_frag",
        _ when name.EndsWith("_STONE", StringComparison.Ordinal) => name[..^"_STONE".Length].ToLowerInvariant(),
        _ => "none"
    };

    public static string ShortPlural(string plural) {
        var words = plural.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return plural;
        int of = Array.IndexOf(words, "OF");
        int inAt = Array.IndexOf(words, "IN");
        return of > 0 ? words[of - 1] : inAt > 0 ? words[inAt - 1] : words[^1];
    }
}
