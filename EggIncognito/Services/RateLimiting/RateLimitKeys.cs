using EggIdentity.Auth;
using EggIdentity.Contract;

namespace EggIncognito.Services.RateLimiting;

public static class RateLimitKeys {
    internal const string NoCfKey = "no-cf";

    public static string ClientIp(HttpContext ctx, bool hosted) {
        string cf = ctx.Request.Headers["CF-Connecting-IP"].ToString();
        if (!string.IsNullOrWhiteSpace(cf)) return cf.Trim();

        if (hosted) return NoCfKey;

        string xff = ctx.Request.Headers["X-Forwarded-For"].ToString();
        return !string.IsNullOrWhiteSpace(xff)
            ? xff.Split(',')[0].Trim()
            : ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    public static string PartitionKey(HttpContext ctx, ICurrentUser user, bool hosted) =>
        user.Current is { IsAuthenticated: true, Id: { } userId }
            ? $"user:{userId}"
            : $"ip:{ClientIp(ctx, hosted)}";

    public static IReadOnlyList<string> TiersFor(ICurrentUser user) {
        var me = user.Current;
        if (!me.IsAuthenticated) return ["Anon"];
        string baseTier = me.IsAtLeast(UserRole.Contributor) ? "Contributor" : "Viewer";
        return me.IsSupporter ? [baseTier, "Supporter"] : [baseTier];
    }
}
