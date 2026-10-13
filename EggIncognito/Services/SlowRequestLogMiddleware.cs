using System.Diagnostics;

namespace EggIncognito.Services;

public sealed class SlowRequestLogMiddleware(RequestDelegate next, ILogger<SlowRequestLogMiddleware> logger,
    IConfiguration config) {
    private readonly long _thresholdMs = config.GetValue("Diagnostics:SlowRequestMs", 300L);

    public async Task InvokeAsync(HttpContext context) {
        long start = Stopwatch.GetTimestamp();
        try {
            await next(context);
        } finally {
            long ms = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (ms >= _thresholdMs && !context.WebSockets.IsWebSocketRequest)
                logger.LogWarning("slow request {Method} {Path}{Query} -> {Status} in {Ms}ms",
                    context.Request.Method, context.Request.Path, context.Request.QueryString,
                    context.Response.StatusCode, ms);
        }
    }
}
