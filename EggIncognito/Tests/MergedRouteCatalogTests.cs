using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public sealed class MergedRouteCatalogTests : IDisposable {
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private const string YamlText = """
                                    routes:
                                      - path: ei/known
                                        request: GetPeriodicalsRequest
                                        response: PeriodicalsResponse
                                    """;

    private RouteCatalog Yaml(string yaml) {
        string p = _tmp.File($"routes-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(p, yaml);
        return new RouteCatalog(p);
    }

    [Fact]
    public void YamlRoute_WinsOverDb() {
        var dbRoute = new RouteInfo("ei/known", "X", "Y", false, false, null, false, false);
        var merged = new MergedRouteCatalog(Yaml(YamlText), new FakeDb(dbRoute));
        Assert.Equal("PeriodicalsResponse", merged.Resolve("ei/known")!.Response);
    }

    [Fact]
    public void DbRoute_FillsNewPath() {
        var dbRoute = new RouteInfo("ei/dbonly", null, "PeriodicalsResponse", false, false, null, false, false);
        var merged = new MergedRouteCatalog(Yaml(YamlText), new FakeDb(dbRoute));
        Assert.Equal("PeriodicalsResponse", merged.Resolve("ei/dbonly")!.Response);
    }

    [Fact]
    public void NullDb_IsYamlOnly() {
        var merged = new MergedRouteCatalog(Yaml(YamlText), null);
        Assert.Null(merged.Resolve("ei/dbonly"));
        Assert.Single(merged.All());
    }

    [Fact]
    public void All_DedupesYamlAndDb() {
        var merged = new MergedRouteCatalog(Yaml(YamlText),
            new FakeDb(new RouteInfo("ei/dbonly", null, "PeriodicalsResponse", false, false, null, false, false)));
        Assert.Equal(2, merged.All().Count);
    }

    [Fact]
    public void YamlRoute_WinsOverBinary() {
        var binaryRoute = new BinaryRouteInfo("ei/known", "getKnown", "X", "Y", false, false, "1.0", null,
            DateTimeOffset.UnixEpoch);
        var merged = new MergedRouteCatalog(Yaml(YamlText), null, new FakeBinary(binaryRoute));
        Assert.Equal("PeriodicalsResponse", merged.Resolve("ei/known")!.Response);
    }

    [Fact]
    public void DbRoute_WinsOverBinary() {
        var dbRoute = new RouteInfo("ei/shared", "X", "FromDb", false, false, null, false, false);
        var binaryRoute = new BinaryRouteInfo("ei/shared", "getShared", "X", "FromBinary", false, false, "1.0", null,
            DateTimeOffset.UnixEpoch);
        var merged = new MergedRouteCatalog(Yaml(YamlText), new FakeDb(dbRoute), new FakeBinary(binaryRoute));
        Assert.Equal("FromDb", merged.Resolve("ei/shared")!.Response);
    }

    [Fact]
    public void BinaryOnlyRoute_AppearsInAll() {
        var binaryRoute = new BinaryRouteInfo("ei/binaryonly", "getBinaryOnly", "X", "Y", false, false, "1.0", null,
            DateTimeOffset.UnixEpoch);
        var merged = new MergedRouteCatalog(Yaml(YamlText), null, new FakeBinary(binaryRoute));
        Assert.Equal(2, merged.All().Count);
        Assert.Equal("Y", merged.Resolve("ei/binaryonly")!.Response);
    }

    private sealed class FakeDb(params RouteInfo[] routes) : IDbRouteProvider {
        private readonly Dictionary<string, RouteInfo> _map = routes.ToDictionary(r => r.Path);
        public RouteInfo? GetDbRoute(string path) => _map.GetValueOrDefault(path);
        public IReadOnlyList<RouteInfo> AllDbRoutes() => routes;
        public void Invalidate() {
        }
    }

    private sealed class FakeBinary(params BinaryRouteInfo[] routes) : IBinaryRouteProvider {
        private readonly Dictionary<string, BinaryRouteInfo> _map = routes.ToDictionary(r => r.Path);
        public BinaryRouteInfo? GetBinaryRoute(string path) => _map.GetValueOrDefault(path);
        public IReadOnlyList<BinaryRouteInfo> AllBinaryRoutes() => routes;
        public void Invalidate() {
        }
    }
}
