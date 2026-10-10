using System.Collections.Frozen;
using EggIdentity.Styles.Theming;

namespace EggIncognito.Services.Theme;

public static class ThemePresets {
    private static readonly FrozenDictionary<string, string> DefaultHex = new Dictionary<string, string> {
        ["bg"] = "#1b1b1f",
        ["panel0"] = "#202027",
        ["panel"] = "#25252b",
        ["panel2"] = "#2e2e36",
        ["fg"] = "#e7e7ea",
        ["muted"] = "#9a9aa5",
        ["accent"] = "#ef7559",
        ["info"] = "#5aa9e6",
        ["ok"] = "#5ec27e",
        ["err"] = "#e0685f",
        ["border"] = "#3a3a44"
    }.ToFrozenDictionary();

    public static ThemeColor DefaultToken(string name) =>
        ThemeColor.FromHex(DefaultHex[name])
        ?? throw new InvalidOperationException($"default hex for theme token '{name}' is not a valid colour");

    public static readonly ThemeModel Default = Build("Default", "default", []);

    public static readonly ThemeModel Forest = Build("Forest", "forest",
        new Dictionary<string, ThemeTokenValue> { ["accent"] = new(Hex: "#469a5e") });

    public static readonly ThemeModel Mono = Build("Mono", "mono",
        new Dictionary<string, ThemeTokenValue> { ["accent"] = new(Hex: "#b4b4c0") });

    public static readonly ThemeModel Violet = Build("Violet", "violet",
        new Dictionary<string, ThemeTokenValue> { ["accent"] = new(Hex: "#b48ef0") },
        new ThemeChroma(SurfaceTint: 4, GlowRadius: 12, GlowAlpha: 35, HueRotate: new ThemeHueRotate()));

    public static readonly ThemeModel Gold = Build("Gold", "gold",
        new Dictionary<string, ThemeTokenValue> { ["accent"] = new(Hex: "#d9a441") },
        new ThemeChroma(GradientHueShift: -25, HueRotate: new ThemeHueRotate()));

    public static readonly ThemeModel Prism = Build("Prism", "prism",
        new Dictionary<string, ThemeTokenValue> { ["accent"] = new(L: 0.85, C: 0.16, H: 90) },
        new ThemeChroma(HueRotate: new ThemeHueRotate(true, 40)));

    public static readonly IReadOnlyList<ThemeModel> All = [Default, Forest, Mono, Violet, Gold, Prism];

    public static ThemeModel? BySlug(string? slug) =>
        All.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.Ordinal));

    private static ThemeModel Build(string name, string slug,
        Dictionary<string, ThemeTokenValue> overrides, ThemeChroma? chroma = null) {
        var tokens = new Dictionary<string, ThemeTokenValue>(StringComparer.Ordinal);
        foreach (var (key, hex) in DefaultHex) tokens[key] = new ThemeTokenValue(Hex: hex);
        foreach (var (key, value) in overrides) tokens[key] = value;
        return new ThemeModel(ThemeSchema.Current, name, slug, ThemeSchema.CurrentVersion,
            tokens, (chroma ?? ThemeChroma.None).Clamped(), "");
    }
}
