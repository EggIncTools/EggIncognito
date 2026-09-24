using System.Globalization;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using EggIdentity.Fallback;
using EggIncognito.Data.Services;
using EggIncognito.Logging;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace EggIncognito.Startup;

public static class HostSetup {
    public static FileLoggerProvider AddAppHosting(this WebApplicationBuilder builder) {
        if (!builder.Environment.IsDevelopment() && !builder.Environment.IsProduction())
            builder.Configuration.AddUserSecrets(typeof(HostSetup).Assembly, true);

        if (!builder.Environment.IsDevelopment()
            && File.Exists(Path.Combine(AppContext.BaseDirectory,
                $"{builder.Environment.ApplicationName}.staticwebassets.runtime.json"))) {
            builder.WebHost.UseStaticWebAssets();
        }

        if (builder.Configuration["EGGINCOGNITO_TEST_DBFREE"] == "1"
            && string.IsNullOrEmpty(builder.Configuration["TestDbOptIn"])) {
            builder.Configuration["ConnectionStrings:Postgres"] = "";
        }

        string logsDir = builder.Configuration["LogsPath"] ?? Path.Combine(AppContext.BaseDirectory, "logs");
        string startupStamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var fileLogProvider = new FileLoggerProvider(logsDir, startupStamp);
        builder.Logging.AddProvider(fileLogProvider);
        builder.WebHost.ConfigureKestrel(ConfigureKestrel);
        return fileLogProvider;
    }

    public static void AddWebServices(this WebApplicationBuilder builder) {
        builder.Services.AddControllers(o => o.Filters.Add<ApiAccessFilter>());
        builder.Services.Configure<RouteOptions>(o => o.ConstraintMap.Add("eins", typeof(EiNamespaceRouteConstraint)));
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents()
            .AddHubOptions(o => o.MaximumReceiveMessageSize = 2 * 1024 * 1024);

        builder.Services.Configure<ForwardedHeadersOptions>(o => {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedProto
                                 | ForwardedHeaders.XForwardedHost
                                 | ForwardedHeaders.XForwardedFor;
            o.KnownProxies.Clear();
            o.KnownIPNetworks.Clear();
        });
        builder.Services.AddAppRateLimiter(builder.Configuration);
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddEggIdentityFallback(
            new FallbackBranding("EggIncognito", FallbackBrandTokens.Tokens, AuthClaims.RoleClaim));
        builder.Services.AddHttpClient("inspector", c => {
            c.DefaultRequestHeaders.Add("User-Agent",
                "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
            c.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler {
            AutomaticDecompression = DecompressionMethods.GZip
        });
    }

    private static void ConfigureKestrel(WebHostBuilderContext context, KestrelServerOptions opts) {
        string certsPath = context.Configuration["CertsPath"] ?? Path.Combine(AppContext.BaseDirectory, "certs");
        string certFile = Path.Combine(certsPath, "server.crt");
        string keyFile = Path.Combine(certsPath, "server.key");
        if (!File.Exists(certFile) || !File.Exists(keyFile)) {
            opts.ApplicationServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Startup")
                .LogWarning(
                    "No TLS cert pair at {CertsPath} (server.crt + server.key) - custom HTTP/HTTPS ports not bound, using default endpoints.",
                    certsPath);
            return;
        }

        int httpPort = int.TryParse(context.Configuration["HttpPort"], out int hp) ? hp : 8080;
        int httpsPort = int.TryParse(context.Configuration["HttpsPort"], out int sp) ? sp : 8443;
        opts.ListenAnyIP(httpPort);
        opts.ListenAnyIP(httpsPort, o => o.UseHttps(X509Certificate2.CreateFromPemFile(certFile, keyFile)));
    }
}
