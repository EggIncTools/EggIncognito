using EggIdentity.Contract;
using EggIncognito.Capture;
using EggIncognito.Controllers;
using EggIncognito.Models.Admin;
using EggIncognito.Services.DataApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public class AdminControllerTests {
    private static AdminController Controller(UserRole role, string id = "me")
        => new(new FakeUser(role: role, discordId: id), new EmptyServices(),
            new CaptureSessionManager(HostedCaptureOptions.Defaults(), (_, _, _) => throw new NotSupportedException()),
            new GameDataStore(new EmptyScopeFactory(), NullLogger<GameDataStore>.Instance));

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

    private sealed class EmptyServices : IServiceProvider {
        public object? GetService(Type t) => null;
    }

    private sealed class EmptyScopeFactory : IServiceScopeFactory {
        public IServiceScope CreateScope() => throw new NotSupportedException();
    }
}
