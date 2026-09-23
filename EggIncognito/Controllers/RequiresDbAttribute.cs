using EggIncognito.Data.Services;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EggIncognito.Controllers;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequiresAttribute<T>(string error) : Attribute, IAsyncResourceFilter where T : class {
    public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next) {
        if (context.HttpContext.RequestServices.GetRequiredService<IServiceProviderIsService>()
            .IsService(typeof(T))) return next();
        context.Result = new ObjectResult(new ApiError(error, null, 503)) { StatusCode = 503 };
        return Task.CompletedTask;
    }
}

public sealed class RequiresDbAttribute() : RequiresAttribute<EggIncognitoDbContext>("no database configured");
