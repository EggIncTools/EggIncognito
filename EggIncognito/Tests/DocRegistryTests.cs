using EggIncognito.Core.Services;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public sealed class DocRegistryTests : IDisposable {
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private const string YamlText = """
                                    routes:
                                      - path: ei/first_contact_secure
                                        request: EggIncFirstContactRequest
                                        requestWrapped: true
                                        response: EggIncFirstContactResponse
                                        responseWrapped: true
                                      - path: ei/get_periodicals
                                        request: GetPeriodicalsRequest
                                        response: PeriodicalsResponse
                                    """;

    private DocRegistry Build() {
        string path = _tmp.Combine($"docreg-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, YamlText);
        var routes = new RouteCatalog(path);
        return new DocRegistry(new ProtoReflection(), routes);
    }

    [Fact]
    public void Roots_HasTheTwoKinds() {
        var roots = Build().Roots();
        var titles = roots.Select(r => r.Title).ToList();
        Assert.Equal(new[] { "Messages", "Endpoints" }, titles);
    }

    [Fact]
    public void Messages_IncludeKnownTypeWithFieldChildren() {
        var reg = Build();
        var messages = reg.Roots().Single(r => r.Title == "Messages").Children;

        var contract = messages.SingleOrDefault(m => m.Key == "Contract");
        Assert.NotNull(contract);
        Assert.Equal("message", contract.Kind);
        Assert.NotEmpty(contract.Children);
        Assert.All(contract.Children, c => Assert.Equal("field", c.Kind));
    }

    [Fact]
    public void Messages_IncludeNestedTypesWithParents() {
        var messages = Build().Roots().Single(r => r.Title == "Messages").Children;

        var nested = messages.SingleOrDefault(m => m.Key == "Backup.Game");
        Assert.NotNull(nested);
        Assert.Equal("Backup", nested.Parent);
        Assert.NotEmpty(nested.Children);

        var backup = messages.SingleOrDefault(m => m.Key == "Backup");
        Assert.NotNull(backup);
        Assert.Null(backup.Parent);
    }

    [Fact]
    public void Endpoints_IncludeKnownRouteWithLinkedTypes() {
        var reg = Build();
        var endpoints = reg.Roots().Single(r => r.Title == "Endpoints").Children;

        var route = endpoints.SingleOrDefault(e => e.Key == "ei/first_contact_secure");
        Assert.NotNull(route);
        Assert.Equal("endpoint", route.Kind);
        Assert.Contains("EggIncFirstContactRequest", route.Summary);
        Assert.Contains("EggIncFirstContactResponse", route.Summary);
        Assert.Contains(route.Children, c => c.Kind == "message" && c.Key == "EggIncFirstContactRequest");
        Assert.Contains(route.Children, c => c.Kind == "message" && c.Key == "EggIncFirstContactResponse");
    }

    [Fact]
    public void Find_ResolvesMessageAndMisses() {
        var reg = Build();
        Assert.NotNull(reg.Find("message", "Contract"));
        Assert.Null(reg.Find("config", "AppMode"));
        Assert.Null(reg.Find("nope", "nope"));
    }
}
