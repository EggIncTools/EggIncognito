namespace EggIncognito.Models.Devices;

public static class PixelWatchKinds {
    public const string Tap = "tap";
    public const string Double = "double";
    public const string Hold = "hold";

    public static readonly string[] All = [Tap, Double, Hold];

    public static string Normalize(string? kind) {
        if (string.Equals(kind, Double, StringComparison.OrdinalIgnoreCase)) return Double;
        if (string.Equals(kind, Hold, StringComparison.OrdinalIgnoreCase)) return Hold;
        return Tap;
    }

    public static bool IsKnown(string? kind) =>
        kind is not null && Array.Exists(All, k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase));
}
