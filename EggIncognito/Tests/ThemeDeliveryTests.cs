using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class ThemeDeliveryTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _f = f;

    [Fact]
    public async Task AnonymousPage_CarriesNoThemeBlock() {
        var c = _f.CreateClient();
        string html = await c.GetStringAsync("/");
        Assert.DoesNotContain("id=\"egi-theme\"", html);
        Assert.DoesNotContain("data-eggidentity-theme", html);
    }

    [Fact]
    public async Task AnonymousPage_LoadsNoThemeBootScript() {
        var c = _f.CreateClient();
        string html = await c.GetStringAsync("/");
        Assert.DoesNotContain("/interop/themeBoot.js", html);
    }

    [Fact]
    public async Task NoRoute_ServesAThemeStylesheet() {
        var c = _f.CreateClient();
        foreach (string url in new[] { "/theme/1.css", "/api/theme/1.css", "/theme.css" }) {
            var resp = await c.GetAsync(url);
            Assert.NotEqual(System.Net.HttpStatusCode.OK, resp.StatusCode);
        }
    }

    [Fact]
    public async Task CspHeader_IsReportOnlyByDefault() {
        string configured = _f.Services.GetRequiredService<IConfiguration>()["Security:Csp"] ?? "";
        Assert.Equal("", configured.Trim());
        var c = _f.CreateClient();
        var resp = await c.GetAsync("/");
        Assert.True(resp.Headers.Contains("Content-Security-Policy-Report-Only"));
        Assert.False(resp.Headers.Contains("Content-Security-Policy"));
        string policy = Assert.Single(resp.Headers.GetValues("Content-Security-Policy-Report-Only"));
        Assert.Contains("script-src 'self'", policy);
        Assert.Contains("style-src 'self' 'nonce-", policy);
        Assert.Contains("style-src-attr 'unsafe-inline'", policy);
        Assert.Contains("connect-src 'self' ws: wss:", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.Contains("base-uri 'self'", policy);
    }

    [Fact]
    public async Task HtmlDocumentResponse_IsNeverStoreCached() {
        var c = _f.CreateClient();
        var resp = await c.GetAsync("/");
        Assert.True(resp.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task StaticAsset_KeepsItsCaching() {
        var c = _f.CreateClient();
        var resp = await c.GetAsync("/interop/deviceConsole.js");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        Assert.NotEqual(true, resp.Headers.CacheControl?.NoStore);
    }
}
