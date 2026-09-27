using System.Globalization;
using EggIdentity.Bot;
using EggIdentity.Client;
using EggIdentity.Contract;
using EggIncognito.Bot;
using EggIncognito.Core.Services;
using EggIncognito.Services;

namespace EggIncognito.Startup;

public static class BotServices {
    private const string RepoUrlValue = "https://github.com/EggIncTools/EggIncognito";
    private const string AppName = "eggincognito";

    public static void AddBotServices(this WebApplicationBuilder builder, BootFlags boot) {
        builder.Services.AddHttpClient("discord-api", c => c.Timeout = TimeSpan.FromSeconds(8));
        if (boot.BotEnabled)
            builder.Services.AddSingleton<ICaptureCaNotifier, DiscordCaptureCaNotifier>();
        else
            builder.Services.AddSingleton<ICaptureCaNotifier, NoopCaptureCaNotifier>();

        if (!boot.BotEnabled) return;

        var buildInfo = BuildInfo.FromAssembly(RepoUrlValue);
        var startedAt = DateTimeOffset.UtcNow;
        builder.Services.AddSingleton(new RepoUrl(RepoUrlValue));
        builder.Services.AddSingleton<IStatusProvider, StatusSnapshotFactory>();
        builder.Services.AddSingleton(sp => BotConfigFor(sp, builder, boot, buildInfo, startedAt));
        builder.Services.AddSingleton(sp => new EggIncognitoBotHostedService(
            sp.GetRequiredService<BotConfig>(), sp.GetRequiredService<ILogger<EggIncognitoBotHostedService>>(),
            RegistrationFor(sp)));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<EggIncognitoBotHostedService>());
        builder.Services.AddScoped(sp =>
            sp.GetRequiredService<EggIncognitoBotHostedService>().Bot?.ConfigService!);
    }

    private static Func<CancellationToken, Task<DiscordRegistrationResponse?>>? RegistrationFor(IServiceProvider sp) =>
        sp.GetService<IdentityApiClient>() is { } identity
            ? ct => identity.GetDiscordRegistrationAsync(AppName, ct)
            : null;

    private static BotConfig BotConfigFor(
        IServiceProvider sp, WebApplicationBuilder builder, BootFlags boot, BuildInfo buildInfo,
        DateTimeOffset startedAt) {
        var status = sp.GetRequiredService<IStatusProvider>();
        var proto = sp.GetRequiredService<IProtoReflection>();
        var botLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("EggIncognito.Bot");
        var config = builder.Configuration;
        return new BotConfig {
            Name = "EggIncognito",
            Token = boot.BotToken ?? "",
            AppId = config["Discord:ClientId"] ?? "",
            GuildId = config["Discord:GuildId"] ?? "",
            RepoUrl = RepoUrlValue,
            Build = new VerifyInfo {
                Name = "EggIncognito",
                Sha256 = buildInfo.Sha,
                Version = buildInfo.Version,
                Date = buildInfo.BuildDate
            },
            SharedRoleId = config["SHARED_ROLE_ID"] ?? config["Discord:SharedRoleId"] ?? "",
            PostgresConnectionString = boot.DbEnabled ? boot.PgConn! : "",
            DashboardChannelId = config["Discord:DashboardChannelId"] ?? "",
            DashboardProvider = _ => Task.FromResult(DashboardSnapshotFor(status, buildInfo, startedAt, botLogger)),
            DashboardRefreshInterval = TimeSpan.FromMinutes(5),
            GlobalCommands = true,
            Extra = new[] {
                ExtraCommands.HealthCommand(startedAt, botLogger),
                ExtraCommands.StatusCommand(status, botLogger),
                ExtraCommands.EndpointsCommand(status, botLogger),
                ExtraCommands.ProtoCommand(proto, botLogger)
            }
        };
    }

    private static DashboardSnapshot DashboardSnapshotFor(
        IStatusProvider status, BuildInfo buildInfo, DateTimeOffset startedAt, ILogger logger) {
        var snap = new DashboardSnapshot {
            AppName = "EggIncognito",
            Version = buildInfo.Version,
            BuildHash = buildInfo.Sha,
            DeployStatus = "online",
            UptimeSince = startedAt,
            RepoUrl = RepoUrlValue
        };
        try {
            var s = status.Build();
            snap.ExtraFields = new Dictionary<string, string> {
                ["Mode"] = s.Mode,
                ["Devices"] = s.DeviceCount.ToString(CultureInfo.InvariantCulture),
                ["Capture"] = s.CaptureState,
                ["DB"] = s.DbEnabled ? "on" : "off",
                ["Signing"] = s.SigningReady ? "ready" : "unset"
            };
        } catch (Exception ex) {
            logger.LogWarning(ex, "bot dashboard: status snapshot unavailable, posting header fields only");
        }

        return snap;
    }
}
