using EggIdentity.Contract;
using EggIncognito.Controllers;
using EggIncognito.Models.Admin;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace EggIncognito.Tests;

public class AdminControllerTests {
    private static AdminController Controller(UserRole role, string id = "me")
        => new(new FakeUser(role, id), new EmptyServices());

    [Fact]
    public async Task Admin_PassesGate_Then503NoIdentityApi() {
        var r = await Controller(UserRole.Admin).Users(null);
        Assert.Equal(503, ((IStatusCodeActionResult)r).StatusCode);
    }

    [Fact]
    public async Task Admin_SelfDemote_Is400() {
        var r = await Controller(UserRole.Admin).SetUserRole("me", new SetRole("viewer"), null);
        Assert.Equal(400, ((IStatusCodeActionResult)r).StatusCode);
    }

    [Theory]
    [InlineData("superuser")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Admin_SetUnknownRole_Is400(string? role) {
        var r = await Controller(UserRole.Admin).SetUserRole("other", new SetRole(role!), null);
        var bad = Assert.IsType<ObjectResult>(r);
        Assert.Equal(400, bad.StatusCode);
        Assert.Contains("unknown role", bad.Value!.ToString());
    }

    private sealed class FakeUser(UserRole role, string id = "me") : ICurrentUser {
        public bool IsAuthenticated => true;
        public Guid? UserId => null;
        public string? DiscordId => id;
        public string? Username => "u";
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => role;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(role, need);
    }

    private sealed class EmptyServices : IServiceProvider {
        public object? GetService(Type t) => null;
    }
}
