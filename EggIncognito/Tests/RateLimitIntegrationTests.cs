using System.Net;
using Microsoft.AspNetCore.Hosting;

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

    [Fact]
    public async Task AssetEndpoint_IsNotCappedByTheAnonTier() {
        var c = f.CreateClient();
        for (int i = 0; i < 10; i++) {
            var r = await c.GetAsync("/api/v1/data/asset/icon?name=no.such.icon");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, r.StatusCode);
        }
    }

    public sealed class TinyLimitFactory : EgiTestFactory {
        protected override void ConfigureRateLimit(IWebHostBuilder builder) {
            builder.UseSetting("RateLimiting:Enabled", "true");
            builder.UseSetting("RateLimiting:Policies:Write:PermitLimit", "3");
            builder.UseSetting("RateLimiting:Policies:Write:WindowSeconds", "60");
            builder.UseSetting("RateLimiting:Tiers:Anon:PermitLimit", "3");
        }
    }
}
