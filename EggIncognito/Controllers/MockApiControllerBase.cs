using System.Text;
using EggIncognito.Core.Services;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;

namespace EggIncognito.Controllers;

[ApiController]
[ApiAccess(ApiAccessLevel.Public)]
public abstract class MockApiControllerBase(IEndpointStore endpoints, IBehaviorService behaviors) : ControllerBase {
    protected Task<IActionResult> HandleAsync<TRes>(string path, string? data, string? sim = null)
        where TRes : IMessage<TRes>, new() {
        EnforceMockAccess(path);
        if (sim is not null) return Task.FromResult<IActionResult>(SimResult(sim));

        string? eid = EidExtractor.FromData(data);
        var response = endpoints.Fetch<TRes>(path, eid);
        string encoded = Convert.ToBase64String(response.ToByteArray());
        return Task.FromResult<IActionResult>(Content(encoded, "text/html"));
    }

    protected Task<IActionResult> HandleRawAsync(string body, string? sim = null) {
        if (sim is not null) return Task.FromResult<IActionResult>(SimResult(sim));
        return Task.FromResult<IActionResult>(Content(body, "text/plain"));
    }

    private ContentResult SimResult(string sim) {
        var behavior = behaviors.Find(sim);
        if (behavior is null) {
            string[] valid = [.. behaviors.All().Select(b => b.Name)];
            throw new ApiException(
                $"unknown sim '{sim}'",
                "Use one of the valid sim names listed in details, or omit ?sim to get the endpoint response.",
                StatusCodes.Status400BadRequest,
                new { valid });
        }

        foreach (var kvp in behavior.ExtraHeaders ?? new Dictionary<string, string>())
            Response.Headers[kvp.Key] = kvp.Value;
        string bodyStr = behavior.Body is not null
            ? Encoding.UTF8.GetString(behavior.Body())
            : string.Empty;
        string contentType = behavior.HttpStatus is >= 200 and < 300 ? "text/html" : "text/plain";
        return new ContentResult {
            StatusCode = behavior.HttpStatus,
            Content = bodyStr,
            ContentType = contentType
        };
    }

    private void EnforceMockAccess(string path) {
        if (!MockAccessGuard.AdminOnlyHosted.Contains(path)) return;
        var mode = HttpContext.RequestServices.GetRequiredService<IAppMode>();
        var user = HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (MockAccessGuard.Blocks(path, mode, user))
            throw new ApiException("admin role required",
                "This endpoint is restricted to admins on the hosted deployment.",
                StatusCodes.Status403Forbidden);
    }
}
