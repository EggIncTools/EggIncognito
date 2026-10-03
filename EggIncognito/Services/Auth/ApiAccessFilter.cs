using EggIdentity.Auth;
using EggIdentity.Contract;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EggIncognito.Services.Auth;

public sealed class ApiAccessFilter : IAsyncAuthorizationFilter {
    public Task OnAuthorizationAsync(AuthorizationFilterContext context) {
        if (!context.HttpContext.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        var attr = context.ActionDescriptor.EndpointMetadata.OfType<ApiAccessAttribute>().LastOrDefault();
        if (attr is null) {
            context.Result = Deny(500, "endpoint missing access policy");
            return Task.CompletedTask;
        }

        var user = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>().Current;
        switch (attr.Level) {
            case ApiAccessLevel.Public:
                break;
            case ApiAccessLevel.Authenticated:
                if (!user.IsAuthenticated) context.Result = Deny(401, "authentication required");
                break;
            case ApiAccessLevel.Contributor:
                if (!user.IsAtLeast(UserRole.Contributor)) context.Result = Deny(403, "contributor role required");
                break;
            case ApiAccessLevel.Admin:
                if (!user.IsAtLeast(UserRole.Admin)) context.Result = Deny(403, "admin role required");
                break;
        }

        return Task.CompletedTask;
    }

    private static ObjectResult Deny(int status, string error) =>
        new(new ApiError(error, null, status)) { StatusCode = status };
}
