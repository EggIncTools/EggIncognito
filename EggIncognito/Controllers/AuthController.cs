using System.Security.Claims;
using EggIdentity.Auth;
using EggIdentity.Client;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;

namespace EggIncognito.Controllers;

[ApiController]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class AuthController(
    AuthState authState,
    ICurrentUser currentUser,
    ILogger<AuthController> logger) : ControllerBase {
    [HttpPost("/logout")]
    public async Task<IActionResult> Logout() {
        if (!authState.Enabled) return NotFound();
        var session = HttpContext.RequestServices.GetService<SessionCookieOptions>();
        if (session is null) return Redirect("/");

        string? sid = User.FindFirstValue(SessionClaims.SessionId);
        if (!string.IsNullOrEmpty(sid)) {
            var identity = HttpContext.RequestServices.GetService<IdentityApiClient>();
            if (identity is not null)
                try {
                    await identity.RevokeSessionAsync(sid, HttpContext.RequestAborted);
                } catch (HttpRequestException ex) {
                    logger.LogWarning(ex,
                        "logout: shared session {Sid} not revoked, identity API unreachable", sid);
                }
        }

        SessionIssuer.ClearCookie(Response, session);
        return Redirect("/");
    }

    [HttpGet("/api/auth/me")]
    public IActionResult Me() {
        if (!currentUser.IsAuthenticated)
            return Ok(new { authenticated = false });
        return Ok(new {
            authenticated = true,
            discordId = currentUser.DiscordId,
            username = currentUser.Username,
            avatar = currentUser.Avatar
        });
    }
}
