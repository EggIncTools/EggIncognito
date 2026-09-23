using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public sealed class RouteProviderCacheTests {
    private static RouteInfo Route(string path) => new(path, "ReqType", "RespType", true, false, null, false, false);

    private static BinaryRouteInfo BinaryRoute(string path) =>
        new(path, "POST", "ReqType", "RespType", true, false, "1.0", null, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Db_GetDbRoute_BeforeTtlElapses_DoesNotRefetch() {
        var time = new FakeTime();
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
        var time = new FakeTime();
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
        var time = new FakeTime();
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
        var time = new FakeTime();
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
        var time = new FakeTime();
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

    [Fact]
    public void Binary_GetBinaryRoute_BeforeTtlElapses_DoesNotRefetch() {
        var time = new FakeTime();
        int calls = 0;
        var inner = new FakeBinaryRouteProvider(() => {
            calls++;
            return [BinaryRoute("a")];
        });
        var cache = new CachedBinaryRouteProvider(inner, TimeSpan.FromSeconds(10), time);

        cache.GetBinaryRoute("a");
        cache.GetBinaryRoute("a");
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Binary_Invalidate_ForcesRefetch_EvenWithinTtl() {
        var time = new FakeTime();
        int calls = 0;
        var inner = new FakeBinaryRouteProvider(() => {
            calls++;
            return [BinaryRoute("a")];
        });
        var cache = new CachedBinaryRouteProvider(inner, TimeSpan.FromSeconds(10), time);

        cache.GetBinaryRoute("a");
        cache.Invalidate();
        cache.GetBinaryRoute("a");
        Assert.Equal(2, calls);
    }

    private sealed class FakeDbRouteProvider(Func<IReadOnlyList<RouteInfo>> fetch) : IDbRouteProvider {
        public RouteInfo? GetDbRoute(string path) => AllDbRoutes().FirstOrDefault(r => r.Path == path);
        public IReadOnlyList<RouteInfo> AllDbRoutes() => fetch();
        public void Invalidate() {
        }
    }

    private sealed class FakeBinaryRouteProvider(Func<IReadOnlyList<BinaryRouteInfo>> fetch) : IBinaryRouteProvider {
        public BinaryRouteInfo? GetBinaryRoute(string path) => AllBinaryRoutes().FirstOrDefault(r => r.Path == path);
        public IReadOnlyList<BinaryRouteInfo> AllBinaryRoutes() => fetch();
        public void Invalidate() {
        }
    }

    private sealed class FakeTime : TimeProvider {
        private DateTimeOffset _now = new(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan d) => _now += d;
    }
}
