using EggIncognito.Services.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Tests;

public class RateLimitOptionsTests {
    [Fact]
    public void Defaults_AreEnabled_WithSaneValues() {
        var o = RateLimitOptions.Defaults();
        Assert.True(o.Enabled);
        Assert.True(o.Tiers["Anon"].PermitLimit < o.Tiers["Viewer"].PermitLimit);
        Assert.True(o.Tiers["Viewer"].PermitLimit < o.Tiers["Contributor"].PermitLimit);
        Assert.True(o.Policies["Egress"].PermitLimit < o.Policies["Read"].PermitLimit);
        Assert.True(o.Policies["Asset"].PermitLimit > o.Tiers["Anon"].PermitLimit);
    }

    [Fact]
    public void Bind_OverridesDefaults_FromConfig() {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["RateLimiting:Enabled"] = "false",
            ["RateLimiting:Tiers:Anon:PermitLimit"] = "7",
            ["RateLimiting:Tiers:Anon:WindowSeconds"] = "60",
            ["RateLimiting:Tiers:Anon:SegmentsPerWindow"] = "6"
        }).Build();

        var o = RateLimitOptions.Bind(cfg);
        Assert.False(o.Enabled);
        Assert.Equal(7, o.Tiers["Anon"].PermitLimit);
        Assert.Equal(RateLimitOptions.Defaults().Tiers["Viewer"].PermitLimit, o.Tiers["Viewer"].PermitLimit);
    }
}
