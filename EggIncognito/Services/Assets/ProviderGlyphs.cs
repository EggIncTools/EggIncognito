using System.Collections.Frozen;

namespace EggIncognito.Services.Assets;

public static class ProviderGlyphs {
    public static readonly FrozenDictionary<string, string> Overrides = new Dictionary<string, string> {
        ["brand-microsoft"] = """<svg viewBox="0 0 24 24" fill="currentColor" width="100%" height="100%" aria-hidden="true"><path d="M3 3h8v8H3zM13 3h8v8h-8zM3 13h8v8H3zM13 13h8v8h-8z"/></svg>"""
    }.ToFrozenDictionary();
}
