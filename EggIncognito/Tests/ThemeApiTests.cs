using System.Net;
using System.Reflection;
using EggIncognito.Controllers;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Theme;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class ThemeApiTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _f = f;

    [Theory]
    [InlineData(nameof(ThemeController.List), ApiAccessLevel.Authenticated, "read")]
    [InlineData(nameof(ThemeController.Get), ApiAccessLevel.Authenticated, "read")]
    [InlineData(nameof(ThemeController.Save), ApiAccessLevel.Authenticated, "write")]
    [InlineData(nameof(ThemeController.Delete), ApiAccessLevel.Authenticated, "write")]
    [InlineData(nameof(ThemeController.Activate), ApiAccessLevel.Authenticated, "write")]
    [InlineData(nameof(ThemeController.Deactivate), ApiAccessLevel.Authenticated, "write")]
    [InlineData(nameof(ThemeController.Import), ApiAccessLevel.Authenticated, "write")]
    [InlineData(nameof(ThemeController.SaveCss), ApiAccessLevel.Contributor, "write")]
    [InlineData(nameof(ThemeController.GetPolicy), ApiAccessLevel.Admin, "write")]
    [InlineData(nameof(ThemeController.SetPolicy), ApiAccessLevel.Admin, "write")]
    public void Action_DeclaresTheExpectedFloorAndRatePolicy(string action, ApiAccessLevel floor, string policy) {
        var method = typeof(ThemeController).GetMethod(action, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        var declared = method.GetCustomAttribute<ApiAccessAttribute>()?.Level
                       ?? typeof(ThemeController).GetCustomAttribute<ApiAccessAttribute>()?.Level;
        Assert.Equal(floor, declared);
        Assert.Equal(policy, method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Theory]
    [InlineData("GET", "/api/theme", HttpStatusCode.Unauthorized)]
    [InlineData("GET", "/api/theme/some-slug", HttpStatusCode.Unauthorized)]
    [InlineData("PUT", "/api/theme/some-slug", HttpStatusCode.Unauthorized)]
    [InlineData("DELETE", "/api/theme/some-slug", HttpStatusCode.Unauthorized)]
    [InlineData("POST", "/api/theme/some-slug/activate", HttpStatusCode.Unauthorized)]
    [InlineData("POST", "/api/theme/import", HttpStatusCode.Unauthorized)]
    [InlineData("PUT", "/api/theme/css", HttpStatusCode.Forbidden)]
    [InlineData("GET", "/api/theme/policy", HttpStatusCode.Forbidden)]
    [InlineData("PUT", "/api/theme/policy", HttpStatusCode.Forbidden)]
    public async Task AnonymousCaller_IsDenied(string method, string url, HttpStatusCode expected) {
        var c = _f.CreateClient();
        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "PUT" or "POST")
            req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var resp = await c.SendAsync(req);
        Assert.Equal(expected, resp.StatusCode);
    }

    [Fact]
    public void Import_RejectsUnknownFields() {
        string json = ThemePresets.Default.ToJson().TrimEnd().TrimEnd('}')
                      + ", \"surprise\": true }";
        var (model, errors) = ThemeJson.Parse(json);
        Assert.Null(model);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Import_RejectsUnknownSchemaVersion() {
        string json = ThemePresets.Default.ToJson().Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2");
        var (model, errors) = ThemeJson.Parse(json);
        Assert.Null(model);
        Assert.Contains(errors, e => e.Contains("schemaVersion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Import_RejectsUnknownTokenNames() {
        string json = ThemePresets.Default.ToJson().Replace("\"bg\":", "\"warn\":");
        var (model, _) = ThemeJson.Parse(json);
        Assert.Null(model);
    }

    [Fact]
    public void Import_RejectsUnknownSchemaId() {
        string json = ThemePresets.Default.ToJson().Replace("eggidentity-theme/1", "eggidentity-theme/9");
        var (model, _) = ThemeJson.Parse(json);
        Assert.Null(model);
    }

    [Fact]
    public void ModelRoundTrip_IsLossless() {
        foreach (var preset in ThemePresets.All) {
            var (model, errors) = ThemeJson.Parse(preset.ToJson());
            Assert.True(errors.Count == 0, preset.Slug + ": " + string.Join("; ", errors));
            Assert.NotNull(model);
            Assert.Equal(preset.Slug, model.Slug);
            Assert.Equal(preset.Tokens.Count, model.Tokens.Count);
        }
    }
}
