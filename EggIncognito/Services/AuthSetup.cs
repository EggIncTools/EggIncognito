using System.Security.Claims;
using EggIdentity.Auth;
using EggIdentity.Client;
using EggIncognito.Data.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using Microsoft.AspNetCore.Authentication;

namespace EggIncognito.Services;

public static class AuthSetup {
    private static async Task StampSupporterClaim(ClaimsPrincipal principal, HttpContext ctx, CancellationToken ct) {
        if (principal.Identity is not ClaimsIdentity identity) return;
        if (SupporterUserId(principal) is not { } userId) return;
        var api = ctx.RequestServices.GetService<IdentityApiClient>();
        if (api is null) return;

        bool isSupporter;
        try {
            isSupporter = (await api.GetSupporterStatusAsync(userId, ct)).IsSupporter;
        } catch (Exception ex) {
            ctx.RequestServices.GetService<ILoggerFactory>()?
                .CreateLogger("EggIncognito.Auth")
                .LogWarning(ex, "supporter check skipped during claims validation");
            return;
        }

        SupporterClaimsSync.Stamp(identity, isSupporter);
    }

    private static Guid? SupporterUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(AuthClaims.UserIdClaim), out var id)
            ? id
            : principal.EggIdentityUserId();

    public static bool AddEggIdentityAuthIfConfigured(
        this WebApplicationBuilder builder, bool identityApiEnabled, SessionCookieOptions? session) {
        if (!identityApiEnabled || session is null) return false;

        builder.Services.AddAuthentication(EggIdentitySessionDefaults.Scheme)
            .AddEggIdentitySession(session, StampSupporterClaim)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyGen.SchemeName, null);

        builder.Services.AddAuthorization();
        return true;
    }
}
