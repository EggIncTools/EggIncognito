using System.Text.Json;
using EggIdentity.Contract;
using EggIncognito.Controllers;
using EggIncognito.Core.Services;
using EggIncognito.Models.Routes;
using EggIncognito.Services;
using EggIncognito.Services.Routes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public sealed class RouteAdminControllerTests : IDisposable {
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static RouteInfo Route(string path, string? request = "PeriodicalsResponse",
        string? response = "PeriodicalsResponse") =>
        new(path, request, response, false, false, null, false, false);

    private static RouteOverrideInfo Override(string path, string? request = null, string? response = null,
        bool? requestWrapped = null, bool? responseWrapped = null, bool? pathParam = null) =>
        new(path, request, response, requestWrapped, responseWrapped, pathParam, DateTimeOffset.UnixEpoch, null);

    private RouteCatalog YamlWith(params string[] paths) {
        string body = string.Concat(paths.Select(p =>
            $"  - path: {p}\n    request: PeriodicalsResponse\n    response: PeriodicalsResponse\n"));
        string file = _tmp.Combine($"routes-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(file, "routes:\n" + body);
        return new RouteCatalog(file);
    }

    private static RouteAdminController Controller(IRouteCatalog routes, RouteCatalog yamlRoutes,
        IServiceProvider? services = null) {
        var sp = services ?? new ServiceCollection().BuildServiceProvider();
        var overrides = sp.GetService<IRouteOverrideProvider>();
        var report = new RouteCatalogReport(routes, yamlRoutes,
            new NonBinaryRouteCatalog(yamlRoutes, null, overrides), overrides, sp.GetService<IBinaryRouteProvider>());
        return new RouteAdminController(routes, report, new ProtoReflection(), new FakeUser(), sp);
    }

    private static string Json(object? value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public void List_MergesSourceAndOverrides_IncludingOrphan() {
        var routes = new FakeCatalog(Route("ei/yaml_route"), Route("ei/db_route"));
        var yaml = YamlWith("ei/yaml_route");
        var services = new ServiceCollection()
            .AddSingleton<IRouteOverrideProvider>(new FakeOverrides(
                Override("ei/yaml_route", response: "NewResp"),
                Override("ei/orphan_path", response: "X")))
            .BuildServiceProvider();

        var result = Assert.IsType<OkObjectResult>(Controller(routes, yaml, services).List());
        string json = Json(result.Value);

        Assert.Contains("\"path\":\"ei/yaml_route\"", json);
        Assert.Contains("\"source\":\"yaml\"", json);
        Assert.Contains("\"path\":\"ei/db_route\"", json);
        Assert.Contains("\"source\":\"db\"", json);
        Assert.Contains("\"path\":\"ei/orphan_path\"", json);
        Assert.Contains("\"source\":\"orphan\"", json);
        Assert.Contains("\"response\":\"NewResp\"", json);
    }

    [Fact]
    public void List_NoOverrideProvider_EffectiveOnlyFromCatalog() {
        var routes = new FakeCatalog(Route("ei/only_route"));
        var yaml = YamlWith("ei/only_route");

        var result = Assert.IsType<OkObjectResult>(Controller(routes, yaml).List());
        string json = Json(result.Value);

        Assert.Contains("\"path\":\"ei/only_route\"", json);
        Assert.Contains("\"override\":null", json);
    }

    [Fact]
    public async Task Put_UnknownPath_404() {
        var routes = new FakeCatalog(Route("ei/known"));
        var r = await Controller(routes, YamlWith()).UpsertAsync("ei/missing",
            new UpsertRouteOverride(null, "PeriodicalsResponse", null, null, null));
        Assert.IsType<NotFoundObjectResult>(r);
    }

    [Fact]
    public async Task Put_AllFieldsNull_400() {
        var routes = new FakeCatalog(Route("ei/known"));
        var r = await Controller(routes, YamlWith()).UpsertAsync("ei/known",
            new UpsertRouteOverride(null, null, null, null, null));
        Assert.IsType<BadRequestObjectResult>(r);
    }

    [Fact]
    public async Task Put_UnknownRequestType_400() {
        var routes = new FakeCatalog(Route("ei/known"));
        var r = await Controller(routes, YamlWith()).UpsertAsync("ei/known",
            new UpsertRouteOverride("NotARealProtoType", null, null, null, null));
        Assert.IsType<BadRequestObjectResult>(r);
    }

    [Fact]
    public async Task Put_UnknownResponseType_400() {
        var routes = new FakeCatalog(Route("ei/known"));
        var r = await Controller(routes, YamlWith()).UpsertAsync("ei/known",
            new UpsertRouteOverride(null, "NotARealProtoType", null, null, null));
        Assert.IsType<BadRequestObjectResult>(r);
    }

    [Fact]
    public async Task Put_ValidBody_NoDb_503() {
        var routes = new FakeCatalog(Route("ei/known"));
        var r = await Controller(routes, YamlWith()).UpsertAsync("ei/known",
            new UpsertRouteOverride(null, "PeriodicalsResponse", null, null, null));
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(503, sc.StatusCode);
    }

    [Fact]
    public async Task Delete_NoProvider_503() {
        var routes = new FakeCatalog(Route("ei/known"));
        var r = await Controller(routes, YamlWith()).DeleteAsync("ei/known");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(503, sc.StatusCode);
    }

    [Fact]
    public async Task Delete_ProviderRegisteredButNoDb_Still503_NeverFalseNegative404() {
        var routes = new FakeCatalog(Route("ei/known"));
        var services = new ServiceCollection()
            .AddSingleton<IRouteOverrideProvider>(new FakeOverrides())
            .BuildServiceProvider();
        var r = await Controller(routes, YamlWith(), services).DeleteAsync("ei/known");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(503, sc.StatusCode);
    }

    [Fact]
    public void ListBinary_NoProvider_503() {
        var routes = new FakeCatalog(Route("ei/known"));
        var r = Controller(routes, YamlWith()).ListBinary();
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(503, sc.StatusCode);
    }

    [Fact]
    public void ListBinary_ReturnsDiscoveredRowsAndReliableDrift() {
        var routes = new FakeCatalog();
        var binaryRoute = new BinaryRouteInfo("ei/known", "getKnown", "X", "Y", true, false, "1.37", null,
            DateTimeOffset.UnixEpoch);
        var services = new ServiceCollection()
            .AddSingleton<IBinaryRouteProvider>(new FakeBinary(binaryRoute))
            .BuildServiceProvider();

        var result = Assert.IsType<OkObjectResult>(Controller(routes, YamlWith("ei/known"), services).ListBinary());
        string json = Json(result.Value);

        Assert.Contains("\"discovered\":1", json);
        Assert.Contains("\"path\":\"ei/known\"", json);
        Assert.Contains("\"field\":\"requestWrapped\"", json);
        Assert.Contains("\"reliable\":true", json);
    }

    [Fact]
    public void ListBinary_BinaryOnlyRoute_FlaggedAsNew() {
        var routes = new FakeCatalog();
        var binaryRoute = new BinaryRouteInfo("ei/discovered", "getDiscovered", "X", "Y", false, false, "1.37", null,
            DateTimeOffset.UnixEpoch);
        var services = new ServiceCollection()
            .AddSingleton<IBinaryRouteProvider>(new FakeBinary(binaryRoute))
            .BuildServiceProvider();

        var result = Assert.IsType<OkObjectResult>(Controller(routes, YamlWith(), services).ListBinary());
        string json = Json(result.Value);

        Assert.Contains("\"newCount\":1", json);
        Assert.Contains("\"field\":\"new\"", json);
    }

    [Fact]
    public void ListBinary_MultiPlatformRows_JoinsProvenanceVersionDescThenPlatform() {
        var routes = new FakeCatalog();
        var services = new ServiceCollection()
            .AddSingleton<IBinaryRouteProvider>(new FakeBinary(
                new BinaryRouteInfo("ei/a", "getA", "X", "Y", false, false, "1.37.1", "ios",
                    DateTimeOffset.UnixEpoch),
                new BinaryRouteInfo("ei/b", "getB", "X", "Y", false, false, "1.37.2", "android",
                    DateTimeOffset.UnixEpoch),
                new BinaryRouteInfo("ei/c", "getC", "X", "Y", false, false, "1.37.2", "android",
                    DateTimeOffset.UnixEpoch)))
            .BuildServiceProvider();

        var result = Assert.IsType<OkObjectResult>(Controller(routes, YamlWith(), services).ListBinary());
        string json = Json(result.Value);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("android 1.37.2 + ios 1.37.1", doc.RootElement.GetProperty("binaryVersion").GetString());
        Assert.Contains("\"platform\":\"android\"", json);
        Assert.Contains("\"platform\":\"ios\"", json);
    }

    [Fact]
    public void ListBinary_RowsWithoutPlatform_ProvenanceIsVersionOnly() {
        var routes = new FakeCatalog();
        var services = new ServiceCollection()
            .AddSingleton<IBinaryRouteProvider>(new FakeBinary(
                new BinaryRouteInfo("ei/a", "getA", "X", "Y", false, false, "1.37", null, DateTimeOffset.UnixEpoch)))
            .BuildServiceProvider();

        var result = Assert.IsType<OkObjectResult>(Controller(routes, YamlWith(), services).ListBinary());
        string json = Json(result.Value);

        Assert.Contains("\"binaryVersion\":\"1.37\"", json);
        Assert.Contains("\"platform\":null", json);
    }

    [Fact]
    public void ListBinary_EditedRoute_DriftSuppressed() {
        var routes = new FakeCatalog();
        var binaryRoute = new BinaryRouteInfo("ei/known", "getKnown", "X", "Y", true, false, "1.37", null,
            DateTimeOffset.UnixEpoch);
        var services = new ServiceCollection()
            .AddSingleton<IBinaryRouteProvider>(new FakeBinary(binaryRoute))
            .AddSingleton<IRouteOverrideProvider>(new FakeOverrides(Override("ei/known", response: "Y")))
            .BuildServiceProvider();

        var result = Assert.IsType<OkObjectResult>(Controller(routes, YamlWith("ei/known"), services).ListBinary());
        string json = Json(result.Value);

        Assert.Contains("\"driftCount\":0", json);
        Assert.DoesNotContain("\"field\":\"requestWrapped\"", json);
    }

    private sealed class FakeCatalog(params RouteInfo[] routes) : IRouteCatalog {
        private readonly Dictionary<string, RouteInfo> _map = routes.ToDictionary(r => r.Path, StringComparer.Ordinal);
        public IReadOnlyList<RouteInfo> All() => routes;
        public RouteInfo? Resolve(string path) => _map.GetValueOrDefault(path);
    }

    private sealed class FakeOverrides(params RouteOverrideInfo[] overrides) : IRouteOverrideProvider {
        private readonly Dictionary<string, RouteOverrideInfo> _map =
            overrides.ToDictionary(o => o.Path, StringComparer.Ordinal);
        public IReadOnlyDictionary<string, RouteOverrideInfo> Snapshot() => _map;
        public void Invalidate() {
        }
    }

    private sealed class FakeBinary(params BinaryRouteInfo[] routes) : IBinaryRouteProvider {
        public BinaryRouteInfo? GetBinaryRoute(string path) => routes.FirstOrDefault(r => r.Path == path);
        public IReadOnlyList<BinaryRouteInfo> AllBinaryRoutes() => routes;
        public void Invalidate() {
        }
    }

    private sealed class FakeUser : ICurrentUser {
        public bool IsAuthenticated => true;
        public Guid? UserId => null;
        public string? DiscordId => "tester";
        public string? Username => "tester";
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => UserRole.Admin;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(Role, need);
    }
}
