using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;

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
