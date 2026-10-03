using System.Net;
using EggIdentity.Auth;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class SealedProxyTests {
    private static SealedProxy NewProxy(SealedProxyOptions options, IHttpClientFactory? factory = null)
        => new(options, factory ?? new StubHttpFactory());

    private static SealedProxyOptions Configured(string? user = null, string? pass = null) => new() {
        UpstreamUrl = "http://proxy.internal:8888",
        Username = user,
        Password = pass
    };

    [Fact]
    public void IsConfigured_EmptyUpstream_False()
        => Assert.False(NewProxy(new SealedProxyOptions()).IsConfigured);

    [Fact]
    public void IsConfigured_WithUpstream_True()
        => Assert.True(NewProxy(Configured()).IsConfigured);

    private static ICurrentUser User(bool supporter, string? discordId = "tester") =>
        new FakeUser(Guid.NewGuid(), DiscordId: discordId, Supporter: supporter).Accessor();

    [Fact]
    public async Task CanUse_Unconfigured_False()
        => Assert.False(await NewProxy(new SealedProxyOptions()).CanUseAsync(User(true)));

    [Fact]
    public async Task CanUse_Anonymous_False()
        => Assert.False(await NewProxy(Configured()).CanUseAsync(new AnonymousUser()));

    [Fact]
    public async Task CanUse_NonSupporter_False()
        => Assert.False(await NewProxy(Configured()).CanUseAsync(User(false)));

    [Fact]
    public async Task CanUse_SupporterWithoutDiscordId_False()
        => Assert.False(await NewProxy(Configured()).CanUseAsync(User(true, discordId: null)));

    [Fact]
    public async Task CanUse_Supporter_True()
        => Assert.True(await NewProxy(Configured()).CanUseAsync(User(true)));

    [Fact]
    public void CreateEgressClient_UsesNamedEgressClient() {
        var factory = new StubHttpFactory();
        var proxy = NewProxy(Configured(), factory);
        _ = proxy.CreateEgressClient();
        Assert.Equal(SealedProxy.EgressClientName, factory.LastName);
    }

    [Fact]
    public void BuildProxy_EmptyUrl_Null()
        => Assert.Null(SealedProxy.BuildProxy(new SealedProxyOptions()));

    [Fact]
    public void BuildProxy_InvalidUrl_Null()
        => Assert.Null(SealedProxy.BuildProxy(new SealedProxyOptions { UpstreamUrl = "not a url" }));

    [Fact]
    public void BuildProxy_ValidUrl_NoCreds_ProxyWithoutCredentials() {
        var proxy = Assert.IsType<WebProxy>(SealedProxy.BuildProxy(Configured()));
        Assert.Null(proxy.Credentials);
        Assert.Equal("http://proxy.internal:8888/", proxy.Address?.ToString());
    }

    [Fact]
    public void BuildProxy_ValidUrl_WithCreds_ProxyCarriesCredentials() {
        var proxy = Assert.IsType<WebProxy>(SealedProxy.BuildProxy(Configured("user", "pass")));
        var cred = Assert.IsType<NetworkCredential>(proxy.Credentials);
        Assert.Equal("user", cred.UserName);
        Assert.Equal("pass", cred.Password);
    }

}
