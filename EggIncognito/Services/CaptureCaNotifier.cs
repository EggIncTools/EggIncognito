using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EggIncognito.Services;

public interface ICaptureCaNotifier {
    Task<bool> SendSetupAsync(CaptureSetupDm dm, CancellationToken ct);
}

public sealed record CaptureSetupDm(
    string DiscordId,
    byte[] CerBytes,
    string ProxyHost,
    int Port);

public sealed class NoopCaptureCaNotifier : ICaptureCaNotifier {
    public Task<bool> SendSetupAsync(CaptureSetupDm dm, CancellationToken ct) => Task.FromResult(false);
}

public sealed class DiscordCaptureCaNotifier(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<DiscordCaptureCaNotifier> logger)
    : ICaptureCaNotifier {
    private const string ProfileFile = "eggincognito-capture.mobileconfig";

    public async Task<bool> SendSetupAsync(CaptureSetupDm dm, CancellationToken ct) {
        string? token = config["Discord:BotToken"];
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(dm.DiscordId) || dm.CerBytes.Length == 0) {
            logger.LogWarning(
                "capture setup DM: not attempted (token {HasToken}, discordId {HasId}, ca {CaBytes} bytes)",
                !string.IsNullOrWhiteSpace(token), !string.IsNullOrWhiteSpace(dm.DiscordId), dm.CerBytes.Length);
            return false;
        }

        try {
            var http = httpFactory.CreateClient("discord-api");
            string? channelId = await OpenDmAsync(http, token, dm.DiscordId, logger, ct);
            if (channelId is null) return false;

            byte[] profile = MobileConfig.BuildCaProfile(dm.CerBytes, dm.DiscordId);
            return await PostAsync(http, token, channelId, profile, ProfileFile, BuildMessage(dm), logger, ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "Capture setup DM to {DiscordId} failed; fail-closed", dm.DiscordId);
            return false;
        }
    }

    internal static string BuildMessage(CaptureSetupDm dm) =>
        $"""
         **Hosted capture is live.**

         **1. Install the CA** (attached). iOS: open it, then Settings > General > About > Certificate Trust Settings, turn it on. Android: needs root.
         **2. Set Wi-Fi proxy to Manual:**
         Server `{dm.ProxyHost}`
         Port `{dm.Port}`
         Auth off
         **3. Open Egg, Inc.**
         """;

    private static async Task<string?> OpenDmAsync(HttpClient http, string token, string discordId,
        ILogger logger, CancellationToken ct) {
        using var req = DiscordBotApi.Request(HttpMethod.Post, "users/@me/channels", token,
            new StringContent(
                JsonSerializer.Serialize(new { recipient_id = discordId }),
                Encoding.UTF8, "application/json"));
        using var res = await http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) {
            logger.LogWarning("capture setup DM: opening a DM with {DiscordId} returned {Status}: {Body}",
                discordId, (int)res.StatusCode, body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            return id.GetString();
        logger.LogWarning("capture setup DM: no channel id in the Discord response for {DiscordId}", discordId);
        return null;
    }

    private static async Task<bool> PostAsync(
        HttpClient http, string token, string channelId, byte[] profile, string fileName, string content,
        ILogger logger, CancellationToken ct) {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(profile);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/x-apple-aspen-config");
        form.Add(file, "files[0]", fileName);
        form.Add(new StringContent(
            JsonSerializer.Serialize(new { content }), Encoding.UTF8, "application/json"), "payload_json");

        using var req = DiscordBotApi.Request(HttpMethod.Post, $"channels/{channelId}/messages", token, form);
        using var res = await http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode) return true;
        logger.LogWarning("capture setup DM: posting to channel {ChannelId} returned {Status}: {Body}",
            channelId, (int)res.StatusCode, await res.Content.ReadAsStringAsync(ct));
        return false;
    }
}
