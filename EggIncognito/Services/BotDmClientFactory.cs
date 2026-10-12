using EggIdentity.Bot;
using EggIncognito.Bot;

namespace EggIncognito.Services;

public sealed class BotDmClientFactory(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    EggIncognitoBotHostedService? bot = null) {
    public IDiscordDmClient? Create() {
        string? token = bot?.Config.Token is { Length: > 0 } resolved ? resolved : config["Discord:BotToken"];
        return string.IsNullOrWhiteSpace(token) ? null : new DiscordDmClient(httpFactory, token);
    }
}
