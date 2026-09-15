using EggIncognito.Services;
using Microsoft.AspNetCore.Http;

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
        });

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Request.QueryString = new QueryString(query);
        await mw.Invoke(ctx, state);
        return (ctx.Response.StatusCode, ctx.Response.Headers.Location.ToString() is { Length: > 0 } l ? l : null,
            continued);
    }

    [Fact]
    public async Task Code_OnAnyPage_RedirectsClean() {
        var r = await RunAsync(Enabled, "GET", "/protos", "?code=goodcode");
        Assert.Equal(StatusCodes.Status302Found, r.Status);
        Assert.Equal("/protos", r.Location);
        Assert.False(r.Continued);
    }

    [Fact]
    public async Task Code_PreservesOtherQueryParams() {
        var r = await RunAsync(Enabled, "GET", "/protos", "?tab=discord&code=goodcode");
        Assert.Equal("/protos?tab=discord", r.Location);
    }

    [Fact]
    public async Task Error_RedirectsWithLoginErrorFlag() {
        var r = await RunAsync(Enabled, "GET", "/", "?error=login_failed");
        Assert.Equal("/?login_error=1", r.Location);
    }

    [Fact]
    public async Task State_IsStrippedToo() {
        var r = await RunAsync(Enabled, "GET", "/protos", "?code=c&state=s");
        Assert.Equal("/protos", r.Location);
    }

    [Fact]
    public async Task NoAuthParams_PassesThrough() {
        var r = await RunAsync(Enabled, "GET", "/health", "");
        Assert.True(r.Continued);
        Assert.Null(r.Location);
    }

    [Fact]
    public async Task Code_PassesThrough_WhenWidgetDisabled() {
        var r = await RunAsync(new AuthState(false), "GET", "/health", "?code=abc");
        Assert.True(r.Continued);
        Assert.Null(r.Location);
    }

    [Fact]
    public async Task Code_PassesThrough_OnPost() {
        var r = await RunAsync(Enabled, "POST", "/protos", "?code=abc");
        Assert.True(r.Continued);
        Assert.Null(r.Location);
    }
}
