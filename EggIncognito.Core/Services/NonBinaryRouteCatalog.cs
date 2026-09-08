namespace EggIncognito.Core.Services;

public interface INonBinaryRouteCatalog : IRouteCatalog { }

public sealed class NonBinaryRouteCatalog(RouteCatalog yaml, IDbRouteProvider? db, IRouteOverrideProvider? overrides)
    : INonBinaryRouteCatalog {
    private readonly OverlayRouteCatalog _inner = new(new MergedRouteCatalog(yaml, db), overrides);

    public RouteInfo? Resolve(string path) => _inner.Resolve(path);

    public IReadOnlyList<RouteInfo> All() => _inner.All();
}
