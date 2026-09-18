using System.Security.Claims;
using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;

namespace EggIncognito.Tests;

public class CurrentUserTests {
    private static CurrentUser Make(ClaimsPrincipal? principal, string? identityHost = null) {
        var ctx = new DefaultHttpContext();
        if (principal is not null) ctx.User = principal;
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        return new CurrentUser(accessor, new AuthState(false, identityHost));
    }

    [Fact]
    public void Anonymous_IsNotAuthenticated() {
        var u = Make(null);
        Assert.False(u.IsAuthenticated);
        Assert.Null(u.DiscordId);
    }

    [Fact]
    public void Authenticated_ExposesClaims() {
        var id = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "123"), new Claim(ClaimTypes.Name, "alice"),
            new Claim(SessionClaims.Avatar, "/avatars/" + Guid.Empty)
        ], "Discord");
        var u = Make(new ClaimsPrincipal(id));
        Assert.True(u.IsAuthenticated);
        Assert.Equal("123", u.DiscordId);
        Assert.Equal("alice", u.Username);
        Assert.Equal("/avatars/" + Guid.Empty, u.Avatar);
        Assert.Equal("/avatars/" + Guid.Empty, u.AvatarUrl);
    }

    [Fact]
    public void AvatarUrl_IdentityHostConfigured_PrefixesRootRelativePath() {
        var id = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "alice"),
            new Claim(SessionClaims.Avatar, IdentityWire.AvatarPath(Guid.Empty))
        ], "Discord");
        var u = Make(new ClaimsPrincipal(id), "https://id.egginc.tools/");
        Assert.Equal($"https://id.egginc.tools/avatars/{Guid.Empty}", u.AvatarUrl);
    }

    [Fact]
    public void AvatarUrl_NoAvatarClaim_IsNull() {
        var id = new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "Discord");
        var u = Make(new ClaimsPrincipal(id), "https://id.egginc.tools/");
        Assert.Null(u.AvatarUrl);
    }

    [Fact]
    public void Authenticated_ExposesUserId() {
        var guid = Guid.NewGuid();
        var id = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "123"), new Claim(AuthClaims.UserIdClaim, guid.ToString())],
            "Discord");
        var u = Make(new ClaimsPrincipal(id));
        Assert.Equal(guid, u.UserId);
    }

    [Fact]
    public void Authenticated_NoUserIdClaim_UserIdIsNull() {
        var id = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "123")], "Discord");
        var u = Make(new ClaimsPrincipal(id));
        Assert.Null(u.UserId);
    }
}
