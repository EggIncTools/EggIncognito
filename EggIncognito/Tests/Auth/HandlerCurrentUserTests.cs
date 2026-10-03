using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIncognito.Services.Auth;

namespace EggIncognito.Tests.Auth;

public class HandlerCurrentUserTests {
    [Fact]
    public void ApiKeyPrincipal_ResolvesOwnerAsViewer() {
        var owner = Guid.NewGuid();

        var user = ApiKeyAuthenticationHandler.Principal(owner, 7).ToCurrentUser();

        Assert.True(user.IsAuthenticated);
        Assert.Equal(owner, user.Id);
        Assert.Equal(UserRole.Viewer, user.Role);
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Contributor)]
    [InlineData(UserRole.Admin)]
    public void LocalIdentityPrincipal_ResolvesConfiguredRole(UserRole role) {
        var user = LocalIdentityAuthenticationHandler.Principal(new LocalIdentitySettings(role, Supporter: true)).ToCurrentUser();

        Assert.True(user.IsAuthenticated);
        Assert.Equal(LocalIdentitySettings.UserId, user.Id);
        Assert.Equal(role, user.Role);
        Assert.True(user.IsSupporter);
    }
}
