using EggIncognito.Core.Services;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Inspector;
using EggIncognito.Models.Routes;

namespace EggIncognito.Services.Routes;

public interface IRouteCatalogReport {
    IReadOnlyList<RouteRow> Rows();
    RouteBinaryStatus? Binary();
    RouteCatalogSummary Summary();
}

public sealed class RouteCatalogReport(
    IRouteCatalog routes,
    RouteCatalog yaml,
    INonBinaryRouteCatalog nonBinary,
    IRouteOverrideProvider? overrides,
    IBinaryRouteProvider? binary) : IRouteCatalogReport {
    public IReadOnlyList<RouteRow> Rows() {
        var snapshot = Snapshot();
        var matched = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<RouteRow>();

        foreach (var route in routes.All()) {
            snapshot.TryGetValue(route.Path, out var o);
            if (o is not null) matched.Add(route.Path);
            rows.Add(new RouteRow(route.Path, yaml.Resolve(route.Path) is not null ? "yaml" : "db",
                EffectiveInfo.From(route), OverrideOf(o)));
        }

        foreach (var o in snapshot.Values) {
            if (matched.Contains(o.Path)) continue;
            rows.Add(new RouteRow(o.Path, "orphan", null, OverrideOf(o)));
        }

        return rows;
    }

    public RouteBinaryStatus? Binary() {
        if (binary is null) return null;

        var rows = binary.AllBinaryRoutes();
        var edited = new HashSet<string>(Snapshot().Keys, StringComparer.Ordinal);
        var drift = RouteDrift.Compute(nonBinary.All(), rows)
            .Where(d => d.Field == "new" || !edited.Contains(d.Path))
            .ToList();

        return new RouteBinaryStatus(
            rows.Count == 0 ? null : rows.Max(r => r.RefreshedAt),
            ProvenanceOf(rows),
            rows.Count,
            drift.Count(d => d.Field == "new"),
            drift.Count(d => d.Field != "new"),
            [.. rows.Select(BinaryRowOf)],
            drift);
    }

    public RouteCatalogSummary Summary() =>
        new(routes.All().Count, Rows().Count(r => r.Override is not null), Binary()?.DriftCount ?? 0);

    private IReadOnlyDictionary<string, RouteOverrideInfo> Snapshot() =>
        overrides?.Snapshot() ?? new Dictionary<string, RouteOverrideInfo>(StringComparer.Ordinal);

    private static OverrideInfo? OverrideOf(RouteOverrideInfo? o) => o is null
        ? null
        : new OverrideInfo(o.Request, o.Response, o.RequestWrapped, o.ResponseWrapped, o.PathParam, o.UpdatedAt,
            o.UpdatedBy);

    private static RouteBinaryRow BinaryRowOf(BinaryRouteInfo b) =>
        new(b.Path, b.Method, b.Request, b.Response, b.RequestWrapped, b.ResponseWrapped, b.BinaryVersion, b.Platform,
            b.RefreshedAt);

    private static string? ProvenanceOf(IReadOnlyList<BinaryRouteInfo> rows) {
        var pairs = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.BinaryVersion))
            .Select(r => (Platform: r.Platform ?? "", Version: r.BinaryVersion!))
            .Distinct()
            .ToList();
        if (pairs.Count == 0) return null;

        pairs.Sort((a, b) => {
            int cmp = DeviceParsing.CompareVersions(b.Version, a.Version);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Platform, b.Platform);
        });
        return string.Join(" + ",
            pairs.Select(p => p.Platform.Length == 0 ? p.Version : $"{p.Platform} {p.Version}"));
    }
}
