using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests.Auth;

public class ApiAccessFilterTests {
    private static AuthorizationFilterContext Context(string path, UserRole role, params object[] metadata) {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(role != UserRole.Viewer ? new FakeUser(Guid.NewGuid(), role).Accessor() : new AnonymousUser());
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.Request.Path = path;
        var descriptor = new ActionDescriptor { EndpointMetadata = metadata };
        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor), []);
    }

    private static async Task<ObjectResult> Deny(AuthorizationFilterContext ctx) {
        await new ApiAccessFilter().OnAuthorizationAsync(ctx);
        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(result.StatusCode, Assert.IsType<ApiError>(result.Value).Status);
        return result;
    }

    [Fact]
    public async Task ApiPath_WithNoAccessPolicy_Is500() =>
        Assert.Equal(500, (await Deny(Context("/api/x", UserRole.Admin))).StatusCode);

    [Fact]
    public async Task Authenticated_WithAnonymousUser_Is401() =>
        Assert.Equal(401, (await Deny(Context("/api/x", UserRole.Viewer,
            new ApiAccessAttribute(ApiAccessLevel.Authenticated)))).StatusCode);

    [Fact]
    public async Task Admin_WithContributor_Is403() =>
        Assert.Equal(403, (await Deny(Context("/api/x", UserRole.Contributor,
            new ApiAccessAttribute(ApiAccessLevel.Admin)))).StatusCode);

    [Fact]
    public async Task NonApiPath_WithNoAccessPolicy_IsLeftAlone() {
        var ctx = Context("/protos", UserRole.Viewer);
        await new ApiAccessFilter().OnAuthorizationAsync(ctx);
        Assert.Null(ctx.Result);
    }
}
