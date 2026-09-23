using EggIncognito.Controllers;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests.Auth;

public class RequiresAttributeTests {
    private static async Task<(ResourceExecutingContext Context, bool Ran)> Run(IServiceCollection services) {
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var ctx = new ResourceExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), [], []);
        bool ran = false;
        await new RequiresDbAttribute().OnResourceExecutionAsync(ctx, () => {
            ran = true;
            return Task.FromResult<ResourceExecutedContext>(null!);
        });
        return (ctx, ran);
    }

    [Fact]
    public async Task NoDatabase_Is503ApiError() {
        var (ctx, ran) = await Run(new ServiceCollection());
        Assert.False(ran);
        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal("no database configured", Assert.IsType<ApiError>(result.Value).Error);
    }

    [Fact]
    public async Task DatabaseRegistered_RunsAction() {
        var services = new ServiceCollection();
        services.AddScoped(_ => (EggIncognitoDbContext)null!);
        var (ctx, ran) = await Run(services);
        Assert.True(ran);
        Assert.Null(ctx.Result);
    }
}
