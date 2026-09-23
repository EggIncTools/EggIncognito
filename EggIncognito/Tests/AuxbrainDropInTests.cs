using System.Net;
using System.Reflection;
using System.Text.Json;
using EggIncognito.Controllers;
using Ei;
using Google.Protobuf;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Tests;

[Collection(EggIncApiCollection.Name)]
public class AuxbrainDropInTests(EggIncApiFactory factory) {
    private readonly HttpClient _client = factory.CreateClient();

    private static FormUrlEncodedContent EmptyForm() {
        string data = Convert.ToBase64String(new AuthenticatedMessage().ToByteArray());
        return new FormUrlEncodedContent([new KeyValuePair<string, string>("data", data)]);
    }

    [Fact]
    public async Task KnownMockedPath_StillServesCannedProto() {
        var resp = await _client.PostAsync("/ei/first_contact_secure", EmptyForm());
        resp.EnsureSuccessStatusCode();
        string body = await resp.Content.ReadAsStringAsync();
        var result = EggIncFirstContactResponse.Parser.ParseFrom(Convert.FromBase64String(body));
        Assert.Equal("EI0000000000000001", result.EiUserId);
    }

    [Fact]
    public async Task UnmappedPath_InKnownNamespace_Returns200Marker_Never404() {
        var resp = await _client.PostAsync("/ei/zz_totally_unknown", EmptyForm());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("not-mocked", resp.Headers.GetValues("x-eggincognito").Single());
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("ei/zz_totally_unknown", doc.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public async Task UnmockedButTypedCanonicalPath_ReturnsEmptyProto() {
        var resp = await _client.PostAsync("/ei/coop_status_bot", EmptyForm());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(resp.Headers.Contains("x-eggincognito"));
        Assert.Equal("text/html", resp.Content.Headers.ContentType?.MediaType);
        string body = await resp.Content.ReadAsStringAsync();
        var msg = ContractCoopStatusResponse.Parser.ParseFrom(Convert.FromBase64String(body));
        Assert.Equal(new ContractCoopStatusResponse(), msg);
    }

    [Fact]
    public async Task AliasPath_ServesSameBodyAsCanonicalPath() {
        var canonical = await _client.PostAsync("/ei/update_coop_status", EmptyForm());
        var alias = await _client.PostAsync("/ei/update_coop_status_secure", EmptyForm());
        Assert.Equal(HttpStatusCode.OK, alias.StatusCode);
        Assert.False(alias.Headers.Contains("x-eggincognito"));
        Assert.Equal("text/html", alias.Content.Headers.ContentType?.MediaType);

        string canonicalBody = await canonical.Content.ReadAsStringAsync();
        string aliasBody = await alias.Content.ReadAsStringAsync();
        Assert.Equal(canonicalBody, aliasBody);

        var msg = ContractCoopStatusUpdateResponse.Parser.ParseFrom(Convert.FromBase64String(aliasBody));
        Assert.True(msg.Finalized);
        Assert.True(msg.Exists);
    }

    [Fact]
    public async Task OpenApiJson_Parses_AndAgreesWithCatalogPaths() {
        var openApiResp = await _client.GetAsync("/api/openapi.json");
        openApiResp.EnsureSuccessStatusCode();
        using var openApi = JsonDocument.Parse(await openApiResp.Content.ReadAsStringAsync());
        Assert.Equal("3.0.3", openApi.RootElement.GetProperty("openapi").GetString());
        var paths = openApi.RootElement.GetProperty("paths");

        var catalogResp = await _client.GetAsync("/api/catalog");
        catalogResp.EnsureSuccessStatusCode();
        using var catalog = JsonDocument.Parse(await catalogResp.Content.ReadAsStringAsync());
        var entries = catalog.RootElement.EnumerateArray().ToList();
        Assert.True(entries.Count >= 64, $"expected >= 64 catalog entries, got {entries.Count}");

        foreach (var entry in entries) {
            string path = entry.GetProperty("path").GetString()!;
            Assert.True(paths.GetProperty("/" + path).TryGetProperty("post", out _),
                $"catalog path {path} missing from openapi.json");
        }
    }

    [Fact]
    public async Task NamespaceIndex_Ei_ListsOnlyEiRoutes_WithStatusLabels() {
        var resp = await _client.GetAsync("/ei");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("ei", doc.RootElement.GetProperty("namespace").GetString());

        var routes = doc.RootElement.GetProperty("routes").EnumerateArray().ToList();
        Assert.NotEmpty(routes);
        string[] labels = ["ok", "empty", "missing", "not-mocked"];
        foreach (var r in routes) {
            Assert.StartsWith("ei/", r.GetProperty("path").GetString());
            Assert.Contains(r.GetProperty("status").GetString(), labels);
        }
    }

    [Fact]
    public async Task NamespaceIndex_UnknownEiNamespace_Returns404() {
        var resp = await _client.GetAsync("/ei_nope");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Landing_ListsEveryCatalogNamespace() {
        string body = await _client.GetStringAsync("/api");
        var resp = await _client.GetAsync("/api/catalog");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var namespaces = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("namespace").GetString()!)
            .Distinct()
            .ToList();
        Assert.NotEmpty(namespaces);
        foreach (string ns in namespaces) {
            Assert.Contains($"<a href=\"/{ns}\">/{ns}</a>", body);
        }
    }

    [Theory]
    [InlineData("/api")]
    [InlineData("/api/reference")]
    public async Task LandingAndReference_Return200Html(string url) {
        var resp = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/html", resp.Content.Headers.ContentType?.MediaType);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("EggIncognito", body);
    }

    [Fact]
    public void ApiSurfaceController_HasReadRateLimitPolicy() {
        var attr = typeof(ApiSurfaceController)
            .GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("read", attr.PolicyName);
    }
}
