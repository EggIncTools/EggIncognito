using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EggIncognito.Tests;

public class RateLimitIntegrationTests(RateLimitIntegrationTests.TinyLimitFactory f)
    : IClassFixture<RateLimitIntegrationTests.TinyLimitFactory> {
    [Fact]
    public async Task WriteEndpoint_Returns429_AfterLimit() {
        var c = f.CreateClient();
        HttpResponseMessage? rejected = null;
        for (int i = 0; i < 10; i++) {
            var r = await c.PostAsJsonAsync("/api/db/route", new { path = "ei/x", response = "Y" });
            if (r.StatusCode == HttpStatusCode.TooManyRequests) {
                rejected = r;
                break;
            }
        }

        Assert.NotNull(rejected);
        Assert.True(rejected.Headers.Contains("Retry-After"));
    }

    public sealed class TinyLimitFactory : WebApplicationFactory<Program> {
        protected override IHost CreateHost(IHostBuilder builder) {
            builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?> {
                ["NoBrowser"] = "true",
                ["RateLimiting:Policies:Write:PermitLimit"] = "3",
                ["RateLimiting:Policies:Write:WindowSeconds"] = "60",
                ["RateLimiting:Tiers:Anon:PermitLimit"] = "3"
            }));
            return base.CreateHost(builder);
        }
    }
}
