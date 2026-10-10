using EggIdentity.Auth;
using EggIdentity.Client;
using Microsoft.AspNetCore.WebUtilities;

namespace EggIncognito.Services;

public sealed class LoginCallbackMiddleware(RequestDelegate next, ILogger<LoginCallbackMiddleware> logger) {
    public async Task Invoke(HttpContext ctx, AuthState authState) {
        if (!authState.WidgetEnabled || !HttpMethods.IsGet(ctx.Request.Method)) {
            await next(ctx);
            return;
        }

        var q = ctx.Request.Query;
        string code = q["code"].ToString();
        string error = q["error"].ToString();
        if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(error)) {
            await next(ctx);
            return;
        }

        bool failed = !string.IsNullOrEmpty(error);
        if (!failed && ctx.User.Identity?.IsAuthenticated != true) failed = await RedeemFailedAsync(ctx, code);
        ctx.Response.Redirect(StripAuthParams(ctx, failed));
    }

    private async Task<bool> RedeemFailedAsync(HttpContext ctx, string code) {
        var identity = ctx.RequestServices.GetService<IdentityApiClient>();
        var session = ctx.RequestServices.GetService<SessionCookieOptions>();
        if (identity is null || session is null) return false;
        try {
            var r = await identity.RedeemAsync(code, ctx.RequestAborted);
            SessionIssuer.IssueCookie(ctx.Response, session,
                new SessionUser(r.UserId.ToString(), null, r.Role, r.Username, r.Avatar, r.DiscordId), DateTimeOffset.UtcNow);
            return false;
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) {
            logger.LogWarning(ex, "login code redeem failed");
            return true;
        }
    }

    private static string StripAuthParams(HttpContext ctx, bool loginError) {
        var kept = ctx.Request.Query
            .Where(kv => kv.Key is not ("code" or "error" or "state"))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        if (loginError) kept["login_error"] = "1";

        var path = ctx.Request.PathBase + ctx.Request.Path;
        return QueryHelpers.AddQueryString(path, kept
            .SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string?>(kv.Key, v))));
    }
}
