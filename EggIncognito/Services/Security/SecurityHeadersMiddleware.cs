using System.Security.Cryptography;
using System.Text;

namespace EggIncognito.Services.Security;

public sealed class SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration, AuthState auth) {
    public const string NonceKey = "egi.csp.nonce";
    private const string ConfigKey = "Security:Csp";
    private const string ModeOff = "off";
    private const string ModeEnforce = "enforce";
    private const string EnforceHeader = "Content-Security-Policy";
    private const string ReportOnlyHeader = "Content-Security-Policy-Report-Only";
    private const string GameAssetHost = "https://www.auxbrain.com";

    public async Task InvokeAsync(HttpContext context) {
        string mode = (configuration[ConfigKey] ?? "").Trim();
        if (string.Equals(mode, ModeOff, StringComparison.OrdinalIgnoreCase)) {
            await next(context);
            return;
        }

        string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items[NonceKey] = nonce;
        bool enforce = string.Equals(mode, ModeEnforce, StringComparison.OrdinalIgnoreCase);
        context.Response.OnStarting(() => {
            context.Response.Headers.Remove(enforce ? ReportOnlyHeader : EnforceHeader);
            context.Response.Headers[enforce ? EnforceHeader : ReportOnlyHeader] =
                BuildPolicy(context.Items[NonceKey] as string ?? nonce);
            string? contentType = context.Response.ContentType;
            if (contentType is not null && contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                context.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });
        await next(context);
    }

    private string BuildPolicy(string nonce) {
        string identityHost = auth is { IdentityHostUrl.Length: > 0 }
            ? " " + auth.IdentityHostUrl.TrimEnd('/')
            : "";

        var sb = new StringBuilder();
        sb.Append("default-src 'self'; ");
        sb.Append("script-src 'self'; ");
        sb.Append("style-src 'self' 'nonce-").Append(nonce).Append("'; ");
        sb.Append("style-src-attr 'unsafe-inline'; ");
        sb.Append("img-src 'self' data: ").Append(GameAssetHost).Append(identityHost).Append("; ");
        sb.Append("connect-src 'self' ws: wss:; ");
        sb.Append("frame-ancestors 'none'; ");
        sb.Append("object-src 'none'; ");
        sb.Append("base-uri 'self'");
        return sb.ToString();
    }
}
