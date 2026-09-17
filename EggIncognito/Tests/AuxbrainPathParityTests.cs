using EggIncognito.Core.Services;
using EggIncognito.RouteGenerator;

namespace EggIncognito.Tests;

public sealed class AuxbrainPathParityTests {
    private static string RouteMapFile(string name) =>
        Path.Combine(TestPaths.WebProjectDir(), "RouteMap", name);

    [Fact]
    public void Aliases_ParsedByCatalog_IgnoredByGenerator() {
        const string yaml = """
                            routes:
                              - path: ei/update_coop_status
                                requestType: ContractCoopStatusUpdateRequest
                                responseType: ContractCoopStatusUpdateResponse
                                aliases:
                                  - ei/update_coop_status_secure
                              - path: ei/other
                                requestType: ConfigRequest
                                responseType: ConfigResponse
                            """;

        var cat = RouteCatalog.Parse(yaml);
        var gen = RouteParser.Parse(yaml);

        Assert.Equal(["ei/update_coop_status", "ei/other"], cat.Select(r => r.Path));
        Assert.Equal(["ei/update_coop_status_secure"], cat[0].Aliases);
        Assert.Empty(cat[1].Aliases);

        Assert.Equal(cat.Select(r => r.Path), gen.Select(r => r.Path));
        Assert.Equal("ContractCoopStatusUpdateRequest", gen[0].Request);
        Assert.Equal("ContractCoopStatusUpdateResponse", gen[0].Response);
        Assert.False(gen[0].RequestWrapped);
        Assert.False(gen[0].ResponseWrapped);
    }

    [Fact]
    public void AliasListItems_DoNotBleedInto_NextRouteOrKeys() {
        const string yaml = """
                            routes:
                              - path: ei/a
                                request: FooRequest
                                aliases:
                                  - ei/old_a
                                  - ei/older_a
                                pathParam: true
                              - path: ei/b
                                responseType: BarResponse
                            """;

        var cat = RouteCatalog.Parse(yaml);
        Assert.Equal(2, cat.Count);
        Assert.Equal(["ei/old_a", "ei/older_a"], cat[0].Aliases);
        Assert.True(cat[0].PathParam);
        Assert.Empty(cat[1].Aliases);
        Assert.Equal("BarResponse", cat[1].Response);

        var gen = RouteParser.Parse(yaml);
        Assert.Equal(cat.Select(r => r.Path), gen.Select(r => r.Path));
        Assert.True(gen[0].PathParam);
        Assert.Equal("BarResponse", gen[1].Response);
    }

    [Fact]
    public void RealRoutesYaml_HasAliasForRenamedCoopStatusUpdate() {
        var routes = RouteCatalog.Parse(File.ReadAllText(RouteMapFile("routes.yaml")));
        var renamed = routes.Single(r => r.Path == "ei/update_coop_status");
        Assert.Contains("ei/update_coop_status_secure", renamed.Aliases);
        Assert.DoesNotContain(routes, r => r.Path == "ei/update_coop_status_secure");
    }

    [Fact]
    public void RenamedEndpointJson_LoadsUnderNewPath() {
        var src = new FileEndpointSource(Path.Combine(TestPaths.WebProjectDir(), "Endpoints"));
        Assert.NotNull(src.Lookup("ei/update_coop_status", null));
    }
}
