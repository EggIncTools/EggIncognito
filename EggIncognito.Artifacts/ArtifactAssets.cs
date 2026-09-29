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
}
