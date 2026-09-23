using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class AuthModeTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _factory = f;

    [Fact]
    public async Task Mode_ReportsAuthDisabled_WhenNoCreds() {
        var c = _factory.CreateClient();
        string json = await c.GetStringAsync("/api/app/mode");
        Assert.Contains("\"authEnabled\":false", json);
        Assert.Contains("\"user\":null", json);
    }

    [Fact]
    public async Task Logout_404s_WhenAuthOff() {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var r = await c.PostAsync("/logout", null);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Me_ReportsUnauthenticated() {
        var c = _factory.CreateClient();
        string json = await c.GetStringAsync("/api/auth/me");
        Assert.Contains("\"authenticated\":false", json);
    }
}
