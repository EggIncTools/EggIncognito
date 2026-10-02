using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class BlazorShellTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _f = f;

    [Fact]
    public async Task Root_ServesTheBlazorShell() {
        var c = _f.CreateClient();
        var r = await c.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string html = await r.Content.ReadAsStringAsync();
        Assert.Contains("/app.css", html);
        Assert.Contains("blazor.web.js", html);
    }

    [Fact]
    public async Task Shell_HasNoTopBar_AndCarriesTheLegalFooter() {
        var c = _f.CreateClient();
        string html = await c.GetStringAsync("/");
        Assert.DoesNotContain("app-nav", html);
        Assert.Contains("id=\"siteFooter\"", html);
        Assert.Contains("href=\"/terms\"", html);
        Assert.Contains("href=\"/privacy\"", html);
        Assert.Contains("LICENSE", html);
    }
}
