using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;

namespace EggIncognito.Controllers;

public abstract class ApiControllerBase : ControllerBase {
    protected ObjectResult Fail(int status, string error) => StatusCode(status, new ApiError(error, null, status));
    protected ObjectResult Fail(int status, string error, object? details) => StatusCode(status, new ApiError(error, null, status, details));
    protected ObjectResult Fail(int status, string error, string? resolution, object? details) => StatusCode(status, new ApiError(error, resolution, status, details));
}
