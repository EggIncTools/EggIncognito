using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class AppVersionTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _factory = f;

    [Fact]
    public async Task AppVersion_ReturnsVersionAndSha() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/app/version");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string json = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"version\"", json);
        Assert.Contains("\"sha\"", json);
    }

    [Fact]
    public async Task SharedAppVersion_IsMappedAndNoStore() {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/_app/version");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        string json = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"version\"", json);
    }

    [Fact]
    public async Task RootPage_RendersSharedReconnectModal_NotTheOldWatcher() {
        var c = _factory.CreateClient();
        string html = await c.GetStringAsync("/protos");
        Assert.Contains("id=\"components-reconnect-modal\"", html);
        Assert.Contains("_content/EggIdentity.UI/reconnect.js", html);
        Assert.DoesNotContain("reconnectWatcher.js", html);
    }
}
