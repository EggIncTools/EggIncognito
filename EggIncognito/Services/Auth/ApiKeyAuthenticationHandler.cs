using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIncognito.Data.Services;
using EggIncognito.Services.DataApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EggIncognito.Services.Auth;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder) {
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
        string? key = ExtractKey();
        if (key is null) return AuthenticateResult.NoResult();

        if (Context.RequestServices.GetService(typeof(ApiKeyStore)) is not ApiKeyStore store)
            return AuthenticateResult.NoResult();

        string hash = ApiKeyGen.HashOf(key);
        var row = await store.FindActiveByHashAsync(hash, Context.RequestAborted);
        if (row is null) return AuthenticateResult.Fail("invalid api key");

        var principal = Principal(row.OwnerUserId, row.Id);
        await store.TouchAsync(row.Id, Context.RequestAborted);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, ApiKeyGen.SchemeName));
    }

    internal static ClaimsPrincipal Principal(Guid owner, int keyId) {
        string role = UserRoles.ToName(UserRole.Viewer);
        Claim[] claims = [
            new(AuthClaims.UserIdClaim, owner.ToString()),
            new(AuthClaims.RoleClaim, role),
            new("sub", owner.ToString()),
            new(SessionClaims.Role, role),
            new(ApiKeyGen.Claim, keyId.ToString(CultureInfo.InvariantCulture))
        ];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, ApiKeyGen.SchemeName));
    }

    private string? ExtractKey() {
        string header = Request.Headers["X-Api-Key"].ToString();
        if (!string.IsNullOrWhiteSpace(header)) return header.Trim();

        string auth = Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (auth.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)) {
            string token = auth[bearer.Length..].Trim();
            if (token.StartsWith(ApiKeyGen.Scheme, StringComparison.Ordinal)) return token;
        }

        return null;
    }
}
