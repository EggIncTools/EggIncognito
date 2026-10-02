using System.Collections.Frozen;
using EggIdentity.Styles.Theming;

namespace EggIncognito.Services.Theme;

public static class ThemeTokens {
    public static readonly IReadOnlyList<string> Settable = [
        "bg", "panel0", "panel", "panel2", "fg", "muted", "accent", "info", "ok", "err", "border"
    ];

    public static readonly IReadOnlyList<string> Derived = [
        "--egi-glow", "--egi-panel-tint", "--egi-accent-grad-to", "--egi-hue-shift"
    ];

    public static readonly IReadOnlyList<string> Locked = [
        "accent2", "warn", "border-strong", "border-input", "control-border", "fg-soft", "fg-max",
        "panel-hi", "panel-lo", "panel-glow", "popover", "scrim", "row-alt", "surface-tint",
        "token-literal", "method-read", "method-write", "method-replace", "method-remove",
        "diff-add-bg", "diff-del-bg", "err-soft", "separator", "scroll-track", "scroll-thumb",
        "scroll-thumb-hover", "ultra-from", "ultra-to"
    ];

    public static readonly IReadOnlyList<string> StatusTokens = ["accent", "ok", "err", "info"];

    public static readonly IReadOnlyList<ThemeCatalogEntry> CatalogEntries = [
        new("panel", ".panel", ThemePropertyGroup.Surface),
        new("panel-title", ".panel h2", ThemePropertyGroup.Text),
        new("button", ".btn-mini", ThemePropertyGroup.Full),
        new("button-hover", ".btn-mini:hover", ThemePropertyGroup.Full),
        new("button-primary", ".btn-primary", ThemePropertyGroup.Full),
        new("button-primary-hover", ".btn-primary:hover:not(:disabled)", ThemePropertyGroup.Full),
        new("button-secondary", ".btn-secondary", ThemePropertyGroup.Full),
        new("input", ".reg-filter-input, .protos-filter", ThemePropertyGroup.Full),
        new("input-focus", ".reg-filter-input:focus, .protos-filter:focus", ThemePropertyGroup.Full),
        new("table", ".data-table", ThemePropertyGroup.Surface),
        new("table-header", ".data-table th", ThemePropertyGroup.Text),
        new("table-row", ".data-table tbody tr", ThemePropertyGroup.Surface),
        new("table-row-alt", ".data-table tbody tr:nth-child(even)", ThemePropertyGroup.Surface),
        new("link", ".page a", ThemePropertyGroup.Text),
        new("link-hover", ".page a:hover", ThemePropertyGroup.Text),
        new("code", ".code-chip", ThemePropertyGroup.Text),
        new("scrollbar-thumb", "::-webkit-scrollbar-thumb", ThemePropertyGroup.ColorOnly),
        new("scrollbar-track", "::-webkit-scrollbar-track", ThemePropertyGroup.ColorOnly),
        new("workbench-rail", ".wb-rail", ThemePropertyGroup.Surface),
        new("workbench-entry", ".wb-entry", ThemePropertyGroup.Surface),
        new("workbench-entry-selected", ".wb-entry.selected", ThemePropertyGroup.Surface),
        new("modal-card", ".modal-card", ThemePropertyGroup.Surface)
    ];

    public static readonly ThemeCssCatalog Catalog = new(CatalogEntries);

    public static readonly ThemeTokenRegistry Registry = new ThemeTokenRegistry().Register("info");

    private static readonly FrozenSet<string> SettableSet = Settable.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> LockedSet = Locked.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsSettable(string name) => SettableSet.Contains(name);

    public static bool IsLocked(string name) => LockedSet.Contains(name);

    public static string? CanonicalSettable(string name) =>
        SettableSet.TryGetValue(name, out string? canonical) ? canonical : null;
}
