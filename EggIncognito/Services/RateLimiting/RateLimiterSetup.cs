using System.Globalization;
using System.Threading.RateLimiting;
using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIncognito.Services.DataApi;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Services.RateLimiting;

public static class RateLimiterSetup {
    public static IServiceCollection AddAppRateLimiter(this IServiceCollection services, IConfiguration config) {
        var opts = RateLimitOptions.Bind(config);
        if (!opts.Enabled) {
            services.AddRateLimiter(o => {
                o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    RateLimitPartition.GetNoLimiter("disabled"));
                foreach (string policy in (string[])["egress", "write", "read", "fetch", "data"])
                    o.AddPolicy(policy, _ => RateLimitPartition.GetNoLimiter("disabled"));
            });
            return services;
        }

        services.AddRateLimiter(o => {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            AddPolicy(o, "egress", opts);
            AddPolicy(o, "write", opts);
            AddPolicy(o, "read", opts);

            o.AddPolicy("fetch", ctx => Partition(ctx, "Fetch", opts, false));
            o.AddPolicy("data", ctx => DataPartition(ctx, opts));

            o.OnRejected = async (ctx, ct) => {
                int retry = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra)
                    ? (int)ra.TotalSeconds
                    : FallbackRetryAfterSeconds(ctx.HttpContext, opts);
                ctx.HttpContext.Response.Headers.RetryAfter = retry.ToString(CultureInfo.InvariantCulture);
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    new ApiError("rate_limited", null, 429, new { retryAfterSeconds = retry }), ct);
            };
        });
        return services;
    }

    private static void AddPolicy(RateLimiterOptions o, string policyKey, RateLimitOptions opts) {
        string optionKey = char.ToUpperInvariant(policyKey[0]) + policyKey[1..];
        o.AddPolicy(policyKey, ctx => Partition(ctx, optionKey, opts));
    }

    internal static int FallbackRetryAfterSeconds(HttpContext ctx, RateLimitOptions opts) {
        string? policyName = ctx.GetEndpoint()?.Metadata
            .GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        if (policyName is { Length: > 0 }) {
            string optionKey = char.ToUpperInvariant(policyName[0]) + policyName[1..];
            if (opts.Policies.TryGetValue(optionKey, out var policy)) return policy.WindowSeconds;
        }

        return 60;
    }

    internal static bool IsExempt(ICurrentUser user) => user.Current.IsAtLeast(UserRole.Admin);

    internal static int EffectivePermit(RateLimitOptions opts, IReadOnlyList<string> tierNames,
        string policyOptionKey) {
        int best = tierNames.Max(t => opts.Tiers[t].PermitLimit);
        return Math.Min(opts.Policies[policyOptionKey].PermitLimit, best);
    }

    private static RateLimitPartition<string> Partition(
        HttpContext ctx, string policyOptionKey, RateLimitOptions opts, bool tierCapped = true) {
        var user = ctx.RequestServices.GetRequiredService<ICurrentUser>();
        if (IsExempt(user)) return RateLimitPartition.GetNoLimiter($"admin:{user.Current.DiscordId}");
        bool hosted = ctx.RequestServices.GetRequiredService<IAppMode>().Mode == AppMode.Hosted;
        string key = RateLimitKeys.PartitionKey(ctx, user, hosted);
        var policy = opts.Policies[policyOptionKey];
        int permit = tierCapped
            ? EffectivePermit(opts, RateLimitKeys.TiersFor(user), policyOptionKey)
            : policy.PermitLimit;

        return RateLimitPartition.GetSlidingWindowLimiter($"{policyOptionKey}:{key}", _ =>
            new SlidingWindowRateLimiterOptions {
                PermitLimit = permit,
                Window = TimeSpan.FromSeconds(policy.WindowSeconds),
                SegmentsPerWindow = policy.SegmentsPerWindow,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    }

    private static RateLimitPartition<string> DataPartition(HttpContext ctx, RateLimitOptions opts) {
        var user = ctx.RequestServices.GetRequiredService<ICurrentUser>();
        if (IsExempt(user)) return RateLimitPartition.GetNoLimiter($"admin:{user.Current.DiscordId}");

        if (ctx.Request.RouteValues.TryGetValue("group", out var group) && (string?)group == "asset")
            return Partition(ctx, "Read", opts);

        bool hosted = ctx.RequestServices.GetRequiredService<IAppMode>().Mode == AppMode.Hosted;

        if (ctx.User.FindFirst(ApiKeyGen.Claim) is { } keyClaim) {
            int permit = Math.Min(opts.Policies["Data"].PermitLimit, opts.Tiers["Keyed"].PermitLimit);
            return Sliding($"data:apikey:{keyClaim.Value}", permit, opts.Policies["Data"]);
        }

        if (user.Current is { IsAuthenticated: true, Id: { } uid }) {
            int permit = EffectivePermit(opts, RateLimitKeys.TiersFor(user), "Data");
            return Sliding($"data:user:{uid}", permit, opts.Policies["Data"]);
        }

        var anon = opts.Policies["DataAnon"];
        return Sliding($"data:ip:{RateLimitKeys.ClientIp(ctx, hosted)}", anon.PermitLimit, anon);
    }

    private static RateLimitPartition<string> Sliding(string key, int permit, RateLimit policy) =>
        RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
            new SlidingWindowRateLimiterOptions {
                PermitLimit = permit,
                Window = TimeSpan.FromSeconds(policy.WindowSeconds),
                SegmentsPerWindow = policy.SegmentsPerWindow,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
}
