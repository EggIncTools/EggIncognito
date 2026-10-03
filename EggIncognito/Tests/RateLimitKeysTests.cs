using System.Net;
using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIncognito.Services.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Tests;

public class RateLimitKeysTests {
    private static HttpContext CtxWith(string? cfIp = null, string? xff = null, string? remote = "10.0.0.9") {
        var ctx = new DefaultHttpContext();
        if (cfIp is not null) ctx.Request.Headers["CF-Connecting-IP"] = cfIp;
        if (xff is not null) ctx.Request.Headers["X-Forwarded-For"] = xff;
        if (remote is not null) ctx.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        return ctx;
    }

    [Fact]
    public void ClientIp_PrefersCfHeader() {
        Assert.Equal("1.2.3.4", RateLimitKeys.ClientIp(CtxWith("1.2.3.4", "9.9.9.9"), false));
        Assert.Equal("1.2.3.4", RateLimitKeys.ClientIp(CtxWith("1.2.3.4", "9.9.9.9"), true));
    }

    [Fact]
    public void ClientIp_Local_FallsBackToXffFirstHop_ThenRemote() {
        Assert.Equal("9.9.9.9", RateLimitKeys.ClientIp(CtxWith(null, "9.9.9.9, 8.8.8.8"), false));
        Assert.Equal("10.0.0.9", RateLimitKeys.ClientIp(CtxWith(), false));
    }

    [Fact]
    public void ClientIp_Hosted_IgnoresXff_UsesSharedBucket() {
        Assert.Equal(RateLimitKeys.NoCfKey, RateLimitKeys.ClientIp(CtxWith(null, "9.9.9.9"), true));
        Assert.Equal(RateLimitKeys.NoCfKey, RateLimitKeys.ClientIp(CtxWith(null, "8.8.8.8"), true));
        Assert.Equal(RateLimitKeys.NoCfKey, RateLimitKeys.ClientIp(CtxWith(), true));
    }

    [Fact]
    public void PartitionKey_UsesUser_WhenAuthenticated() {
        var ctx = CtxWith("1.2.3.4");
        var userId = Guid.NewGuid();
        var user = new FakeUser(userId, DiscordId: "disc123").Accessor();
        Assert.Equal($"user:{userId}", RateLimitKeys.PartitionKey(ctx, user, false));
    }

    [Fact]
    public void PartitionKey_UsesIp_WhenAnonymous() {
        var ctx = CtxWith("1.2.3.4");
        Assert.Equal("ip:1.2.3.4", RateLimitKeys.PartitionKey(ctx, new AnonymousUser(), false));
    }

    [Fact]
    public void PartitionKey_Hosted_Anonymous_NoCf_SharesBucket() {
        var ctx = CtxWith(null, "6.6.6.6");
        Assert.Equal($"ip:{RateLimitKeys.NoCfKey}", RateLimitKeys.PartitionKey(ctx, new AnonymousUser(), true));
    }

    private static ICurrentUser User(bool auth, UserRole role) =>
        auth ? new FakeUser(Guid.NewGuid(), role, DiscordId: "x").Accessor() : new AnonymousUser();

    [Theory]
    [InlineData(false, UserRole.Viewer, "Anon")]
    [InlineData(true, UserRole.Viewer, "Viewer")]
    [InlineData(true, UserRole.Contributor, "Contributor")]
    [InlineData(true, UserRole.Admin, "Contributor")]
    public void TiersFor_MapsRole(bool auth, UserRole role, string expected) => Assert.Equal(new[] { expected },
        RateLimitKeys.TiersFor(User(auth, role)));

    [Fact]
    public void TiersFor_SupporterViewer_IncludesSupporter() {
        var user = new FakeUser(Guid.NewGuid(), DiscordId: "x", Supporter: true).Accessor();
        Assert.Equal(new[] { "Viewer", "Supporter" }, RateLimitKeys.TiersFor(user));
    }

    [Theory]
    [InlineData("Anon", "Egress", 10)]
    [InlineData("Viewer", "Egress", 10)]
    [InlineData("Contributor", "Egress", 10)]
    [InlineData("Anon", "Write", 30)]
    [InlineData("Viewer", "Write", 60)]
    [InlineData("Contributor", "Write", 60)]
    public void EffectivePermit_IsMinOfPolicyAndTier(string tier, string policy, int expected) => Assert.Equal(expected,
        RateLimiterSetup.EffectivePermit(RateLimitOptions.Defaults(), new[] { tier }, policy));

    [Theory]
    [InlineData(false, UserRole.Viewer, false)]
    [InlineData(true, UserRole.Viewer, false)]
    [InlineData(true, UserRole.Contributor, false)]
    [InlineData(true, UserRole.Admin, true)]
    public void IsExempt_OnlyAdmins(bool auth, UserRole role, bool expected) => Assert.Equal(expected,
        RateLimiterSetup.IsExempt(User(auth, role)));

    [Fact]
    public void FallbackRetryAfter_UsesMatchedPolicyWindow() {
        var opts = new RateLimitOptions(
            true,
            new Dictionary<string, RateLimit> { ["Anon"] = new(30, 60, 6) },
            new Dictionary<string, RateLimit> { ["Egress"] = new(10, 33, 6) });

        var ctx = new DefaultHttpContext();
        ctx.SetEndpoint(new Endpoint(null,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute("egress")), "test"));
        Assert.Equal(33, RateLimiterSetup.FallbackRetryAfterSeconds(ctx, opts));
    }

    [Fact]
    public void FallbackRetryAfter_Is60_WithoutPolicyMetadata() {
        var opts = RateLimitOptions.Defaults();
        Assert.Equal(60, RateLimiterSetup.FallbackRetryAfterSeconds(new DefaultHttpContext(), opts));

        var ctx = new DefaultHttpContext();
        ctx.SetEndpoint(new Endpoint(null,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute("nonesuch")), "test"));
        Assert.Equal(60, RateLimiterSetup.FallbackRetryAfterSeconds(ctx, opts));
    }
}
