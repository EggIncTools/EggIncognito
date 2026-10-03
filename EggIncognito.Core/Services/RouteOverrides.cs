using EggIdentity.Resilience;
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

public sealed class CachedRouteOverrideProvider(
    Func<IReadOnlyDictionary<string, RouteOverrideInfo>> fetch,
    TimeSpan ttl,
    TimeProvider? time = null,
    ILogger? logger = null) : IRouteOverrideProvider, IDisposable {
    private readonly TtlSnapshot<IReadOnlyDictionary<string, RouteOverrideInfo>> _cache =
        RouteSnapshot.Create(() => fetch().Values, r => r.Path, ttl, time, logger);

    public IReadOnlyDictionary<string, RouteOverrideInfo> Snapshot() => _cache.Get();

    public void Invalidate() => _cache.Invalidate();

    public void Dispose() => _cache.Dispose();
}
