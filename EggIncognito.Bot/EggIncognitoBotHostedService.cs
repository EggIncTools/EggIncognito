using EggIdentity.Bot;
using EggIdentity.Contract;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Bot;

public sealed class EggIncognitoBotHostedService(
    BotConfig cfg,
    ILogger<EggIncognitoBotHostedService> logger,
    Func<CancellationToken, Task<DiscordRegistrationResponse?>>? registration = null) : IHostedService {
    public EggIdentityBot? Bot { get; private set; }

    public BotConfig Config { get; private set; } = cfg;

    public async Task StartAsync(CancellationToken cancellationToken) {
        Config = await ResolveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(Config.Token)) {
            logger.LogInformation("bot: no Discord token from the suite or local settings - bot stays off");
            return;
        }

        try {
            Bot = await EggIdentityBot.StartAsync(Config);
        } catch (Exception ex) {
            logger.LogError(ex, "bot: failed to start - continuing without the bot");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken) {
        if (Bot is not null) await Bot.DisposeAsync();
    }

    private async Task<BotConfig> ResolveAsync(CancellationToken ct) {
        if (registration is null) return Config;
        try {
            return await registration(ct) is { } reg ? Config.WithRegistration(reg) : Config;
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                         or System.Text.Json.JsonException) {
            logger.LogWarning(ex, "bot: suite Discord registration unavailable - using local Discord settings");
            return Config;
        }
    }
}
