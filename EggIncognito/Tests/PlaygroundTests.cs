using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class PlaygroundTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _factory = f;

    private static readonly HttpStatusCode[] CatchAllFallThrough = [
        HttpStatusCode.NotFound,
        HttpStatusCode.MethodNotAllowed,
        HttpStatusCode.BadRequest
    ];

    private static void AssertRouteGone(HttpResponseMessage r) =>
        Assert.True(Array.IndexOf(CatchAllFallThrough, r.StatusCode) >= 0,
            $"{r.RequestMessage?.Method} {r.RequestMessage?.RequestUri} still resolves to a route: "
            + $"{(int)r.StatusCode}");

    [Fact]
    public async Task Playground_Route_IsARedirectStub() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/playground");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string html = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("playgroundCanvas", html);
        Assert.DoesNotContain("Contributor access required", html);
        Assert.DoesNotContain("href=\"playground\"", html);
        Assert.DoesNotContain("href=\"admin\"", html);
        Assert.Contains("id=\"siteFooter\"", html);
    }

    [Fact]
    public async Task Devices_Status_IsPublic_ForAnonymousHosts() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/devices/status");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Devices_ListMeshes_RequiresAdmin() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/devices/some-device/list-meshes");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task ShellsObjects_ReturnsShape() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/shells/objects?platform=ios&type=chicken");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string json = await r.Content.ReadAsStringAsync();

        Assert.Contains("\"ok\"", json);
        Assert.Contains("\"type\":\"chicken\"", json);
    }

    [Fact]
    public async Task PlaygroundRecorder_ScriptIsServed() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/interop/playgroundRecorder.js");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string body = await r.Content.ReadAsStringAsync();
        Assert.Contains("renderAtPhase", body);
        Assert.Contains("playground-loop.gif", body);
    }

    [Fact]
    public async Task FarmCatalog_Responds() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/farm/catalog?platform=ios");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string json = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"ok\"", json);
        Assert.Contains("\"platform\":\"ios\"", json);
    }

    [Fact]
    public async Task FarmShowcase_ListsTheFixturePresets() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/farm/showcase");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string json = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"count\":141", json);
    }

    [Fact]
    public async Task FarmLayout_WithoutInputs_ReportsWhatIsMissing() {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/farm/layout", new { });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string json = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"ok\":false", json);
        Assert.Contains("\"diagnostics\"", json);
    }

    [Fact]
    public async Task FarmMesh_RejectsATraversalStem() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/farm/mesh/..%2Fegginc");
        Assert.True(r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"expected the stem guard to reject, got {(int)r.StatusCode}");
    }

    [Fact]
    public async Task EnvDesigns_List_RequiresContributor() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/env/designs");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task EnvDesigns_Read_RequiresContributor() {
        var c = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/env/designs/test")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/env/designs/test/versions")).StatusCode);
    }

    [Fact]
    public async Task EnvDesigns_VersionPayload_RouteIsGone() {
        var c = _factory.CreateClient();
        AssertRouteGone(await c.GetAsync("/api/env/designs/test/versions/1"));
    }

    [Fact]
    public async Task EnvDesigns_Save_RequiresContributor() {
        var c = _factory.CreateClient();
        var body = new StringContent("{\"payload\":\"{}\"}", Encoding.UTF8, "application/json");
        var r = await c.PutAsync("/api/env/designs/test", body);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task EnvDesigns_Delete_RequiresContributor() {
        var c = _factory.CreateClient();
        var r = await c.DeleteAsync("/api/env/designs/test");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Config_List_Responds() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Config_Ingest_RequiresAdmin() {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/config/ios/ingest", new { configResponseBase64 = "" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task InspectorBuild_ConfigRequest_WithClonedRinfo_Is200() {
        var c = _factory.CreateClient();
        var body = new {
            path = "ei/get_config",
            requestType = "ConfigRequest",
            fields = new { },
            env = new {
                eiUserId = "EI5862923193024512",
                clientVersion = 72,
                version = "1.35.7",
                build = "111343",
                platform = "DROID",
                country = "US",
                language = "en"
            },
            wrap = true,
            salt = ""
        };
        var r = await c.PostAsJsonAsync("/api/inspector/build", body);
        string json = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"HTTP {(int)r.StatusCode}: {json}");
        Assert.Contains("finalFormBody", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Config_IngestJson_RequiresAdmin() {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/config/ios/ingest-json", new { json = "{}" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Shells_List_Responds() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/shells?platform=ios");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Poke_RequiresAdmin() {
        var c = _factory.CreateClient();
        var r = await c.PostAsync("/api/devices/x/poke", null);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task HarvestState_RequiresAdmin() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/devices/x/harvest");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task DeviceMeshRoutes_AreGone() {
        var c = _factory.CreateClient();
        AssertRouteGone(await c.PostAsync("/api/devices/x/precache-meshes", null));
        AssertRouteGone(await c.PostAsync("/api/devices/x/pull-meshes", null));
        AssertRouteGone(await c.GetAsync("/api/devices/x/mesh/ei_farm_ground"));
        AssertRouteGone(await c.GetAsync("/api/devices/x/cached-meshes"));
        AssertRouteGone(await c.DeleteAsync("/api/devices/x/cached-meshes/ei_farm_ground"));
    }

    [Fact]
    public async Task Console_Endpoints_RequiresAdmin() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/console/endpoints");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Periodicals_Route_MergedIntoProtosData() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/periodicals");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string html = await r.Content.ReadAsStringAsync();
        Assert.Contains("id=\"siteFooter\"", html);

        Assert.DoesNotContain(">Periodicals</button>", html);
    }
}
