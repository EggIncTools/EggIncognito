using EggIdentity.Resilience;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Core.Services;

internal static class RouteSnapshot {
    public static TtlSnapshot<IReadOnlyDictionary<string, T>> Create<T>(
        Func<IEnumerable<T>> fetch, Func<T, string> keyOf, TimeSpan ttl, TimeProvider? time, ILogger? logger) =>
        new(ttl, () => ToOrdinalDict(fetch(), keyOf), time,
            ex => logger?.LogSnapshotRefreshFailed(ex, typeof(T).Name, ttl)) {
            Fallback = new Dictionary<string, T>(StringComparer.Ordinal)
        };

    private static Dictionary<string, T> ToOrdinalDict<T>(IEnumerable<T> source, Func<T, string> keyOf) =>
        source.GroupBy(keyOf, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
}

public sealed class CachedDbRouteProvider(IDbRouteProvider inner, TimeSpan ttl, TimeProvider? time = null,
    ILogger? logger = null) : IDbRouteProvider, IDisposable {
    private readonly TtlSnapshot<IReadOnlyDictionary<string, RouteInfo>> _cache =
        RouteSnapshot.Create(inner.AllDbRoutes, r => r.Path, ttl, time, logger);

    public RouteInfo? GetDbRoute(string path) => _cache.Get().GetValueOrDefault(path);

    public IReadOnlyList<RouteInfo> AllDbRoutes() => _cache.Get().Values.ToList();

    public void Invalidate() => _cache.Invalidate();

    public void Dispose() => _cache.Dispose();
}

public sealed class CachedBinaryRouteProvider(IBinaryRouteProvider inner, TimeSpan ttl, TimeProvider? time = null,
    ILogger? logger = null) : IBinaryRouteProvider, IDisposable {
    private readonly TtlSnapshot<IReadOnlyDictionary<string, BinaryRouteInfo>> _cache =
        RouteSnapshot.Create(inner.AllBinaryRoutes, b => b.Path, ttl, time, logger);

    public BinaryRouteInfo? GetBinaryRoute(string path) => _cache.Get().GetValueOrDefault(path);

    public IReadOnlyList<BinaryRouteInfo> AllBinaryRoutes() => _cache.Get().Values.ToList();

    public void Invalidate() => _cache.Invalidate();

    public void Dispose() => _cache.Dispose();
}

internal static partial class TtlSnapshotCacheLog {
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "{Kind} snapshot refresh failed; serving the previous snapshot for another {Ttl}")]
    internal static partial void LogSnapshotRefreshFailed(this ILogger logger, Exception ex, string kind, TimeSpan ttl);
}
