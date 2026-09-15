using EggIncognito.Controllers;
using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public class HostAllowlistTests {
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("www.auxbrain.com")]
    [InlineData("auxbrain.com")]
    [InlineData("auxbrainhome.appspot.com")]
    [InlineData("ctx-dot-auxbrainhome.appspot.com")]
    [InlineData("some-service-dot-auxbrainhome.appspot.com")]
    public void Allows_LegitHosts(string host) =>
        Assert.True(InspectorApiController.IsAllowedHost(host));

    [Theory]
    [InlineData("evil.com")]
    [InlineData("auxbrainhome.appspot.com.evil.com")]
    [InlineData("evil.com-dot-auxbrainhome.appspot.com")]
    [InlineData("notauxbrain.com")]
    [InlineData("-dot-auxbrainhome.appspot.com")]
    public void Rejects_BadHosts(string host) =>
        Assert.False(InspectorApiController.IsAllowedHost(host));

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public void Localhost_IsAllowed_ButNotAuxbrain(string host) {
        Assert.True(InspectorApiController.IsAllowedHost(host));
        Assert.False(AuxbrainHosts.IsAuxbrain(host));
    }

    [Fact]
    public void SelfHost_IsAllowed_OnlyWhenItMatches() {
        const string self = "eggincognito.egginc.tools";
        Assert.True(InspectorApiController.IsAllowedHost(self, self));
        Assert.True(InspectorApiController.IsAllowedHost("EggIncognito.EggInc.TOOLS", self));
        Assert.False(InspectorApiController.IsAllowedHost(self));
        Assert.False(InspectorApiController.IsAllowedHost("evil.com", self));
    }
}
