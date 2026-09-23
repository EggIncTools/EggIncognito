using EggIncognito.Data.Services;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EggIncognito.Controllers;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresDbAttribute : Attribute, IAsyncResourceFilter {
    public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next) {
        if (context.HttpContext.RequestServices.GetRequiredService<IServiceProviderIsService>()
            .IsService(typeof(EggIncognitoDbContext))) return next();
        context.Result = new ObjectResult(new ApiError("no database configured", null, 503)) { StatusCode = 503 };
        return Task.CompletedTask;
    }
}
