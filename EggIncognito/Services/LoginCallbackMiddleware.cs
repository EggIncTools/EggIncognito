using Microsoft.AspNetCore.WebUtilities;

namespace EggIncognito.Services;

public sealed class LoginCallbackMiddleware(RequestDelegate next) {
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

        ctx.Response.Redirect(StripAuthParams(ctx, !string.IsNullOrEmpty(error)));
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
