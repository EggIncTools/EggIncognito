using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public class AuthCallbackTests {
    private static readonly AuthState Enabled =
        new(true, "http://identity.local", SessionActive: true);

    private static async Task<(int Status, string? Location, bool Continued)> RunAsync(
        AuthState state, string method, string path, string query) {
        bool continued = false;
        var mw = new LoginCallbackMiddleware(_ => {
            continued = true;
            return Task.CompletedTask;
        }, NullLogger<LoginCallbackMiddleware>.Instance);

        var ctx = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Request.QueryString = new QueryString(query);
        await mw.Invoke(ctx, state);
        return (ctx.Response.StatusCode, ctx.Response.Headers.Location.ToString() is { Length: > 0 } l ? l : null,
            continued);
    }

    [Fact]
    public async Task Code_OnAnyPage_RedirectsClean() {
        var (status, location, continued) = await RunAsync(Enabled, "GET", "/protos", "?code=goodcode");
        Assert.Equal(StatusCodes.Status302Found, status);
        Assert.Equal("/protos", location);
        Assert.False(continued);
    }

    [Fact]
    public async Task Code_PreservesOtherQueryParams() {
        var (_, location, _) = await RunAsync(Enabled, "GET", "/protos", "?tab=discord&code=goodcode");
        Assert.Equal("/protos?tab=discord", location);
    }

    [Fact]
    public async Task Error_RedirectsWithLoginErrorFlag() {
        var (_, location, _) = await RunAsync(Enabled, "GET", "/", "?error=login_failed");
        Assert.Equal("/?login_error=1", location);
    }

    [Fact]
    public async Task State_IsStrippedToo() {
        var (_, location, _) = await RunAsync(Enabled, "GET", "/protos", "?code=c&state=s");
        Assert.Equal("/protos", location);
    }

    [Fact]
    public async Task NoAuthParams_PassesThrough() {
        var (_, location, continued) = await RunAsync(Enabled, "GET", "/health", "");
        Assert.True(continued);
        Assert.Null(location);
    }

    [Fact]
    public async Task Code_PassesThrough_WhenWidgetDisabled() {
        var (_, location, continued) = await RunAsync(new AuthState(false), "GET", "/health", "?code=abc");
        Assert.True(continued);
        Assert.Null(location);
    }

    [Fact]
    public async Task Code_PassesThrough_OnPost() {
        var (_, location, continued) = await RunAsync(Enabled, "POST", "/protos", "?code=abc");
        Assert.True(continued);
        Assert.Null(location);
    }
}
