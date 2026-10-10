using Ei;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class ShellCatalog {
    private const string CdnBase = AuxbrainHosts.Origin + "/dlc";

    public static readonly IReadOnlyList<double> DefaultChickenAnchor = [0.0, 0.428, -0.11, 1.06];

    public static IReadOnlyList<Shell> FromCatalog(DLCCatalog catalog) {
        var shells = new List<Shell>();
        if (catalog is null) return shells;

        foreach (var s in catalog.Shells) {
            var piece = s.PrimaryPiece ?? s.Pieces.FirstOrDefault();
            if (piece?.Dlc is not { } dlc) continue;
            string? url = AssetUrl(dlc);
            if (url is null) continue;
            shells.Add(new Shell(s.Identifier ?? "", NullIfEmpty(s.Name), piece.AssetType.ToString(),
                url, NullIfEmpty(dlc.Checksum), s.ModifiedGeometry, NullIfEmpty(s.SetIdentifier)));
        }

        foreach (var o in catalog.ShellObjects) {
            var piece = o.Pieces.Where(p => p.Dlc is not null).MinBy(p => p.Lod);
            if (piece?.Dlc is not { } dlc) continue;
            string? url = AssetUrl(dlc);
            if (url is null) continue;
            shells.Add(new Shell(o.Identifier ?? "", NullIfEmpty(o.Name), o.AssetType.ToString(),
                url, NullIfEmpty(dlc.Checksum), false));
        }

        return shells;
    }

    public static IReadOnlyList<Shell> ForAssetType(DLCCatalog catalog, string assetType) =>
        FromCatalog(catalog).Where(s => string.Equals(s.AssetType, assetType, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public static Shell? ById(DLCCatalog catalog, string identifier) =>
        FromCatalog(catalog).FirstOrDefault(s => string.Equals(s.Identifier, identifier, StringComparison.Ordinal));

    public static IReadOnlyList<ShellObject> Objects(DLCCatalog catalog) {
        var objs = new List<ShellObject>();
        if (catalog is null) return objs;
        foreach (var o in catalog.ShellObjects) {
            var piece = o.Pieces.Where(p => p.Dlc is not null).MinBy(p => p.Lod);
            if (piece?.Dlc is not { } dlc) continue;
            string? url = AssetUrl(dlc);
            if (url is null) continue;
            bool isChicken = o.AssetType == ShellSpec.Types.AssetType.Chicken;
            var anchor = o.Metadata.Count > 0
                ? o.Metadata.ToList()
                : isChicken && !o.NoHats
                    ? DefaultChickenAnchor
                    : [];
            objs.Add(new ShellObject(o.Identifier ?? "", NullIfEmpty(o.Name), o.AssetType.ToString(),
                url, NullIfEmpty(dlc.Checksum), anchor, o.NoHats));
        }

        return objs;
    }

    public static IReadOnlyList<ShellObject> Chickens(DLCCatalog catalog) =>
        Objects(catalog).Where(o => string.Equals(o.AssetType, "Chicken", StringComparison.OrdinalIgnoreCase)).ToList();

    public static IReadOnlyList<ShellObject> Hats(DLCCatalog catalog) =>
        Objects(catalog).Where(o => string.Equals(o.AssetType, "Hat", StringComparison.OrdinalIgnoreCase)).ToList();

    public static ShellObject? ObjectById(DLCCatalog catalog, string identifier) =>
        Objects(catalog).FirstOrDefault(o => string.Equals(o.Identifier, identifier, StringComparison.Ordinal));

    public static IReadOnlyList<ShellSet> Sets(DLCCatalog catalog) => BuildSets(catalog, catalog?.ShellSets, false);

    public static IReadOnlyList<ShellSet> Decorators(DLCCatalog catalog) =>
        BuildSets(catalog, catalog?.Decorators, true);

    private static List<ShellSet> BuildSets(DLCCatalog? catalog, IEnumerable<ShellSetSpec>? specs, bool decorator) {
        var result = new List<ShellSet>();
        if (catalog is null || specs is null) return result;
        var bySet = FromCatalog(catalog)
            .Where(s => !string.IsNullOrEmpty(s.SetIdentifier))
            .GroupBy(s => s.SetIdentifier!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Shell>)[.. g], StringComparer.Ordinal);
        foreach (var spec in specs) {
            string id = spec.Identifier ?? "";
            if (id.Length == 0) continue;
            var members = bySet.TryGetValue(id, out var m) ? m : [];
            result.Add(new ShellSet(id, NullIfEmpty(spec.Name), decorator, members));
        }

        return result;
    }

    public static string? AssetUrl(DLCItem? dlc) {
        if (dlc is null) return null;
        if (!string.IsNullOrEmpty(dlc.Url)) return dlc.Url;
        if (string.IsNullOrEmpty(dlc.Directory) || string.IsNullOrEmpty(dlc.Name)) return null;
        string? ext = string.IsNullOrEmpty(dlc.Ext) ? "rpoz" : dlc.Ext;
        return $"{CdnBase}/{dlc.Directory}/{dlc.Name}.{ext}";
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    public sealed record Shell(
        string Identifier,
        string? Name,
        string AssetType,
        string Url,
        string? Checksum,
        bool ModifiedGeometry,
        string? SetIdentifier = null);

    public sealed record ShellObject(
        string Identifier,
        string? Name,
        string AssetType,
        string Url,
        string? Checksum,
        IReadOnlyList<double> Anchor,
        bool NoHats);

    public sealed record ShellSet(string Identifier, string? Name, bool Decorator, IReadOnlyList<Shell> Members);
}
