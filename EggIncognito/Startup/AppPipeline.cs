using EggIdentity.Bot;
using EggIdentity.Contract;
using EggIdentity.Db;
using EggIdentity.Fallback;
using EggIdentity.Metrics;
using EggIncognito.Bot;
using EggIncognito.Components;
using EggIncognito.Core.Services;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Devices.Fake;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EggIncognito.Startup;

public static class AppPipeline {
    public static async Task InitializeAsync(this WebApplication app, BootFlags boot) {
        if (boot.FakeDevices) {
            app.Logger.LogWarning(
                "fake device stack active ({Env} plus AppMode.Local plus {Key}): the real device platforms, store " +
                "checkers, process runner and device agent are not registered. Fake devices: {Devices}",
                app.Environment.EnvironmentName, FakeDeviceGate.EnabledKey,
                string.Join(", ", boot.FakeDeviceSettings.Devices.Select(d => $"{d.Id} [{d.Scenario}] {d.Target}")));
        }

        var extensions = app.Services.GetRequiredService<Services.Devices.DeviceExtensionCatalog>();
        if (extensions.Loaded > 0 || extensions.Errors.Count > 0) {
            app.Logger.LogInformation(
                "device extensions: loaded {Count} type(s) from {Source} ({Types}); {Failed} assembly load failure(s)",
                extensions.Loaded, extensions.Source, string.Join(", ", extensions.Types), extensions.Errors.Count);
        }

        if (boot.LocalIdentity is not null) {
            app.Logger.LogWarning(
                "local identity active ({Env} plus AppMode.Local plus {Key}): every session is {User} with role {Role}, " +
                "supporter {Supporter}. EggIdentity is not configured; requests carrying an API key still authenticate " +
                "through the key path.",
                app.Environment.EnvironmentName, LocalIdentityGate.EnabledKey, boot.LocalIdentity.Username,
                boot.LocalIdentity.RoleName, boot.LocalIdentity.Supporter);
        }

        if (!boot.DbEnabled) {
            app.Logger.LogInformation("No ConnectionStrings:Postgres - running file-only (no DB overlay).");
            return;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();
        await db.Database.MigrateAsync();
        await RouteSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<RouteCatalog>());
        await TagSeeder.SeedAsync(db);

        var deviceStore = scope.ServiceProvider.GetService<IDeviceStatusStore>();
        if (deviceStore is not null) {
            var flat = boot.DeviceConfig.Devices
                .Select(d => (d.Id, d.Platform, d.Label, d.Target, d.Package)).ToList();
            await DeviceSeeder.SeedAsync(deviceStore, db, flat);
        }

        app.Logger.LogInformation("Postgres DB layer active: migrated + seeded yaml routes + tags.");
    }

    public static void UseAppPipeline(this WebApplication app, BootFlags boot) {
        app.UseForwardedHeaders();
        app.Use(async (ctx, next) => {
            ctx.Request.Headers.Remove("Sec-WebSocket-Extensions");
            await next();
        });
        app.UseExceptionHandler();
        app.UseMiddleware<Services.Security.SecurityHeadersMiddleware>();
        app.Use(async (ctx, next) => {
            if (ctx.Request.Host.Host.StartsWith("protos.", StringComparison.OrdinalIgnoreCase)
                && ctx.Request.Path == "/") {
                ctx.Request.Path = "/protos";
            }

            await next();
        });

        app.UseStaticFiles();
        app.UseRouting();
        if (boot.AuthEnabled) {
            app.UseAuthentication();
            app.UseMiddleware<ApiKeyResolutionMiddleware>();
            app.UseAuthorization();
            app.UseMiddleware<LoginCallbackMiddleware>();
        } else if (boot.LocalIdentityOn) {
            app.UseAuthentication();
            app.UseMiddleware<ApiKeyResolutionMiddleware>();
            app.UseAuthorization();
        }

        app.UseEggIdentityFallback();
        app.UseAntiforgery();
        app.UseRateLimiter();
        app.UseEggIdentityRequestMetrics();
    }

    public static void MapAppEndpoints(this WebApplication app, BootFlags boot) {
        app.MapControllers();
        if (boot.SyncIngestEnabled) {
            var ingest = app.Services.GetRequiredService<NewVersionIngestService>();
            app.MapPost("/events/new-version",
                    NewVersionHandler.Build(boot.EventSecret!, evt => ingest.HandleAsync(evt)))
                .RequireRateLimiting("write");
        }

        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        app.MapGet("/health", () => Results.Ok());
        app.MapGet("/api/app/mode", (IAppMode m, AuthState auth, ICurrentUser user) =>
            Results.Ok(new {
                mode = m.Mode.ToString(),
                canCapture = m.CanCapture,
                canWrite = m.CanWrite,
                hostedCapture = m.HostedCaptureEnabled,
                authEnabled = auth.Enabled,
                user = user.IsAuthenticated
                    ? new {
                        user.DiscordId,
                        user.Username,
                        user.Avatar,
                        role = UserRoles.ToName(user.Role),
                        supporter = user.IsSupporter
                    }
                    : null
            }));

        if (!boot.AuthEnabled) return;
        app.MapPost("/api/account/refresh-benefits",
            async (HttpContext http, ICurrentUser user, CancellationToken ct) => {
                if (!user.IsAuthenticated) return Results.Unauthorized();
                await SupporterRefresh.RequestAsync(http, ct);
                return Results.Redirect("/#support");
            }).RequireRateLimiting("read");
    }

    public static async Task RunBotMigrationsAsync(this WebApplication app, BootFlags boot) {
        if (!boot.BotEnabled || !boot.DbEnabled) return;

        await using (var adminConn = await NpgsqlDataSource.Create(boot.PgConn!).OpenConnectionAsync()) {
            await Migrator.MigrateAsync(adminConn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
        }

        var deployDataSource = NpgsqlDataSource.Create(boot.PgConn!);
        var configStore = new ChannelConfigStore(deployDataSource);
        var botCfg = app.Services.GetRequiredService<BotConfig>();
        var hosted = app.Services.GetRequiredService<EggIncognitoBotHostedService>();
        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () => {
            var client = hosted.Bot?.Client;
            if (client is null || !ulong.TryParse(botCfg.GuildId, out ulong guildId)) return;
            var notifier = new DeployNotifier(configStore, client, guildId, botCfg.Name);
            var tracker = new DeployVersionTracker(new DeployStateStore(deployDataSource), notifier);
            try {
                await tracker.CheckAndNotifyAsync(
                    botCfg.Name, Environment.GetEnvironmentVariable("GIT_SHA") ?? "", botCfg.Build.Version,
                    CancellationToken.None);
            } catch (Exception ex) {
                app.Logger.LogWarning(ex, "deploy notify failed");
            }
        }));
    }
}
