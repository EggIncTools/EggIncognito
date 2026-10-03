using System.Security.Claims;
using System.Text.Encodings.Web;
using EggIdentity.Auth;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EggIncognito.Services.Auth;

public sealed class LocalIdentityAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    LocalIdentitySettings settings)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder) {
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        if (ApiKeyResolutionMiddleware.HasKeyHeader(Context))
            return Task.FromResult(AuthenticateResult.NoResult());

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(Principal(settings), LocalIdentityAuth.Scheme)));
    }

    internal static ClaimsPrincipal Principal(LocalIdentitySettings settings) {
        Claim[] claims = [
            new(AuthClaims.UserIdClaim, LocalIdentitySettings.UserId.ToString()),
            new(ClaimTypes.Name, settings.Username),
            new(AuthClaims.RoleClaim, settings.RoleName),
            new("sub", LocalIdentitySettings.UserId.ToString()),
            new(SessionClaims.Name, settings.Username),
            new(SessionClaims.Role, settings.RoleName),
            new(SessionClaims.Supporter, settings.Supporter ? "true" : "false")
        ];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, LocalIdentityAuth.Scheme));
    }
}
