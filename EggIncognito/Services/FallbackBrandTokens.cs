using System.Collections.Frozen;

namespace EggIncognito.Services;

public static class FallbackBrandTokens {
    public static readonly FrozenDictionary<string, string> Tokens = new Dictionary<string, string> {
        ["--color-bg"] = "#1b1b1f",
        ["--color-panel0"] = "#202027",
        ["--color-panel"] = "#25252b",
        ["--color-panel2"] = "#2e2e36",
        ["--color-fg"] = "#e7e7ea",
        ["--color-muted"] = "#9a9aa5",
        ["--color-accent"] = "#ef7559",
        ["--color-accent2"] = "#ef7559",
        ["--color-warn"] = "oklch(87.9% 0.169 91.605)",
        ["--color-ok"] = "#5ec27e",
        ["--color-err"] = "#e0685f",
        ["--color-border"] = "#3a3a44",
    }.ToFrozenDictionary();
}
