using EggIncognito.Services;
using EggIncognito.Services.Predictions.Training;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EggIncognito.Controllers;

public abstract class ApiControllerBase : ControllerBase {
    protected ObjectResult Fail(int status, string error) => StatusCode(status, new ApiError(error, null, status));
    protected ObjectResult Fail(int status, string error, object? details) => StatusCode(status, new ApiError(error, null, status, details));
    protected ObjectResult Fail(int status, string error, string? resolution, object? details) => StatusCode(status, new ApiError(error, resolution, status, details));
    protected void NoStore() => Response.Headers.CacheControl = "no-store";
    protected void NoCache() => Response.Headers.CacheControl = "no-cache";
    protected void PublicCache(int seconds) => Response.Headers.CacheControl = $"public, max-age={seconds}";
    protected void PrivateCache(int seconds) => Response.Headers.CacheControl = $"private, max-age={seconds}";
    protected void Immutable() => Response.Headers.CacheControl = "public, max-age=31536000, immutable";
}

public sealed class PredictionsReadyFilter : IAsyncExceptionFilter {
    public Task OnExceptionAsync(ExceptionContext context) {
        if (context.Exception is not PredictionsNotReadyException ex) return Task.CompletedTask;
        context.Result = new ObjectResult(new ApiError(ex.Message, null, 503)) { StatusCode = 503 };
        context.ExceptionHandled = true;
        return Task.CompletedTask;
    }
}
