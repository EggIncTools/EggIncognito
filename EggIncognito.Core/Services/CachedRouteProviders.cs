using Microsoft.Extensions.Logging;

namespace EggIncognito.Core.Services;

internal sealed class TtlSnapshotCache<T>(
    Func<IReadOnlyList<T>> fetch,
    Func<T, string> keyOf,
    TimeSpan ttl,
    TimeProvider? time = null,
    ILogger? logger = null) {
    public TtlSnapshotCache(
        Func<IReadOnlyDictionary<string, T>> fetchMap,
        Func<T, string> keyOf,
        TimeSpan ttl,
        TimeProvider? time = null,
        ILogger? logger = null) : this(() => fetchMap().Values.ToList(), keyOf, ttl, time, logger) { }

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private IReadOnlyDictionary<string, T> _snapshot = new Dictionary<string, T>(StringComparer.Ordinal);
    private DateTimeOffset? _fetchedAt;

    public IReadOnlyDictionary<string, T> Snapshot() {
        lock (_lock) {
            if (_fetchedAt is { } fetchedAt && _time.GetUtcNow() - fetchedAt < ttl) return _snapshot;
            Refresh();
            return _snapshot;
        }
    }

    public void Invalidate() {
        lock (_lock) {
            _fetchedAt = null;
        }
    }

    private void Refresh() {
        try {
            _snapshot = ToOrdinalDict(fetch());
        } catch (Exception ex) {
            logger?.LogSnapshotRefreshFailed(ex, typeof(T).Name, ttl);
        }
        _fetchedAt = _time.GetUtcNow();
    }

    private Dictionary<string, T> ToOrdinalDict(IReadOnlyList<T> source) {
        var result = new Dictionary<string, T>(source.Count, StringComparer.Ordinal);
        foreach (var item in source) result[keyOf(item)] = item;
        return result;
    }
}

public sealed class CachedDbRouteProvider : IDbRouteProvider {
    private readonly TtlSnapshotCache<RouteInfo> _cache;

    public CachedDbRouteProvider(IDbRouteProvider inner, TimeSpan ttl, TimeProvider? time = null,
        ILogger? logger = null) {
        _cache = new TtlSnapshotCache<RouteInfo>(inner.AllDbRoutes, r => r.Path, ttl, time, logger);
    }

    public RouteInfo? GetDbRoute(string path) => _cache.Snapshot().GetValueOrDefault(path);

    public IReadOnlyList<RouteInfo> AllDbRoutes() => _cache.Snapshot().Values.ToList();

    public void Invalidate() => _cache.Invalidate();
}

public sealed class CachedBinaryRouteProvider : IBinaryRouteProvider {
    private readonly TtlSnapshotCache<BinaryRouteInfo> _cache;

    public CachedBinaryRouteProvider(IBinaryRouteProvider inner, TimeSpan ttl, TimeProvider? time = null,
        ILogger? logger = null) {
        _cache = new TtlSnapshotCache<BinaryRouteInfo>(inner.AllBinaryRoutes, b => b.Path, ttl, time, logger);
    }

    public BinaryRouteInfo? GetBinaryRoute(string path) => _cache.Snapshot().GetValueOrDefault(path);

    public IReadOnlyList<BinaryRouteInfo> AllBinaryRoutes() => _cache.Snapshot().Values.ToList();

    public void Invalidate() => _cache.Invalidate();
}

internal static partial class TtlSnapshotCacheLog {
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "{Kind} snapshot refresh failed; serving the previous snapshot for another {Ttl}")]
    internal static partial void LogSnapshotRefreshFailed(this ILogger logger, Exception ex, string kind, TimeSpan ttl);
}
