using EggIncognito.Core.Services;
using Microsoft.Extensions.Time.Testing;

namespace EggIncognito.Tests;

public sealed class RouteProviderCacheTests {
    private static RouteInfo Route(string path) => new(path, "ReqType", "RespType", true, false, null, false, false);

    [Fact]
    public void Db_GetDbRoute_BeforeTtlElapses_DoesNotRefetch() {
        var time = new FakeTimeProvider(Start);
        int calls = 0;
        var inner = new FakeDbRouteProvider(() => {
            calls++;
            return [Route("a")];
        });
        var cache = new CachedDbRouteProvider(inner, TimeSpan.FromSeconds(10), time);

        cache.GetDbRoute("a");
        cache.GetDbRoute("a");
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Db_GetDbRoute_AfterTtlElapses_Refetches() {
        var time = new FakeTimeProvider(Start);
        int calls = 0;
        var inner = new FakeDbRouteProvider(() => {
            calls++;
            return [Route("a")];
        });
        var cache = new CachedDbRouteProvider(inner, TimeSpan.FromSeconds(10), time);

        cache.GetDbRoute("a");
        time.Advance(TimeSpan.FromSeconds(11));
        cache.GetDbRoute("a");
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Db_Invalidate_ForcesRefetch_EvenWithinTtl() {
        var time = new FakeTimeProvider(Start);
        int calls = 0;
        var inner = new FakeDbRouteProvider(() => {
            calls++;
            return [Route("a")];
        });
        var cache = new CachedDbRouteProvider(inner, TimeSpan.FromSeconds(10), time);

        cache.GetDbRoute("a");
        cache.Invalidate();
        cache.GetDbRoute("a");
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Db_FetchThrowsAfterSuccess_KeepsStaleSnapshot() {
        var time = new FakeTimeProvider(Start);
        bool fail = false;
        var inner = new FakeDbRouteProvider(() => {
            if (fail) throw new InvalidOperationException("db down");
            return [Route("a")];
        });
        var cache = new CachedDbRouteProvider(inner, TimeSpan.FromSeconds(10), time);

        var first = cache.GetDbRoute("a");
        Assert.NotNull(first);

        fail = true;
        cache.Invalidate();
        var second = cache.GetDbRoute("a");
        Assert.Same(first, second);
    }

    [Fact]
    public void Db_UnknownPath_NegativeLookupIsCached() {
        var time = new FakeTimeProvider(Start);
        int calls = 0;
        var inner = new FakeDbRouteProvider(() => {
            calls++;
            return [Route("a")];
        });
        var cache = new CachedDbRouteProvider(inner, TimeSpan.FromSeconds(10), time);

        Assert.Null(cache.GetDbRoute("missing"));
        Assert.Null(cache.GetDbRoute("missing"));
        Assert.Equal(1, calls);
    }

    private sealed class FakeDbRouteProvider(Func<IReadOnlyList<RouteInfo>> fetch) : IDbRouteProvider {
        public RouteInfo? GetDbRoute(string path) => AllDbRoutes().FirstOrDefault(r => r.Path == path);
        public IReadOnlyList<RouteInfo> AllDbRoutes() => fetch();
        public void Invalidate() {
        }
    }

    private static readonly DateTimeOffset Start = new(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);
}
