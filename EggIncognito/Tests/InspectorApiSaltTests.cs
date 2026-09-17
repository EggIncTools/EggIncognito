using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class InspectorApiSaltTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _factory = f;

    private static object BuildBody(string? salt) => new {
        path = "ei/first_contact_secure",
        requestType = "EggIncFirstContactRequest",
        wrap = true,
        fields = new { eiUserId = "EI1234567890123456" },
        env = (object?)null,
        salt
    };

    [Fact]
    public async Task Build_WithSalt_ReportsCanSignTrue() {
        var resp = await _factory.CreateClient().PostAsJsonAsync("/api/inspector/build", BuildBody("test-salt"));
        string bodyText = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"status {(int)resp.StatusCode}: {bodyText}");
        using var doc = JsonDocument.Parse(bodyText);
        Assert.True(doc.RootElement.GetProperty("canSign").GetBoolean());
    }

    [Fact]
    public async Task Build_WithoutSalt_ReportsCanSignFalse() {
        var resp = await _factory.CreateClient().PostAsJsonAsync("/api/inspector/build", BuildBody(null));
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("canSign").GetBoolean());
    }

    [Fact]
    public async Task SameSalt_ProducesStableSignature() {
        var client = _factory.CreateClient();

        async Task<string> BuildOnce() {
            var resp = await client.PostAsJsonAsync("/api/inspector/build", BuildBody("stable-salt"));
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("finalBase64").GetString()!;
        }

        Assert.Equal(await BuildOnce(), await BuildOnce());
    }

    [Fact]
    public async Task EnvDefaults_EndpointIsGone() {
        var resp = await _factory.CreateClient().GetAsync("/api/inspector/env-defaults");
        Assert.False(resp.IsSuccessStatusCode);
        Assert.Contains(resp.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }
}
