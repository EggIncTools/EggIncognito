using Microsoft.Extensions.Logging;

namespace EggIncognito.Core.Services;

public sealed record RouteOverrideInfo(
    string Path,
    string? Request,
    string? Response,
    bool? RequestWrapped,
    bool? ResponseWrapped,
    bool? PathParam,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy);

public interface IRouteOverrideProvider {
    IReadOnlyDictionary<string, RouteOverrideInfo> Snapshot();
    void Invalidate();
}

public sealed class CachedRouteOverrideProvider : IRouteOverrideProvider {
    private readonly TtlSnapshotCache<RouteOverrideInfo> _cache;

    public CachedRouteOverrideProvider(
        Func<IReadOnlyDictionary<string, RouteOverrideInfo>> fetch,
        TimeSpan ttl,
        TimeProvider? time = null,
        ILogger? logger = null) {
        _cache = new TtlSnapshotCache<RouteOverrideInfo>(fetch, r => r.Path, ttl, time, logger);
    }

    public IReadOnlyDictionary<string, RouteOverrideInfo> Snapshot() => _cache.Snapshot();

    public void Invalidate() => _cache.Invalidate();
}
