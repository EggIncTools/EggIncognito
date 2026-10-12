using EggIdentity.Bot;

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
    BotDmClientFactory dms,
    ILogger<DiscordCaptureCaNotifier> logger)
    : ICaptureCaNotifier {
    private const string ProfileFile = "eggincognito-capture.mobileconfig";

    public async Task<bool> SendSetupAsync(CaptureSetupDm dm, CancellationToken ct) {
        var client = dms.Create();
        if (client is null || string.IsNullOrWhiteSpace(dm.DiscordId) || dm.CerBytes.Length == 0) {
            logger.LogWarning(
                "capture setup DM: not attempted (token {HasToken}, discordId {HasId}, ca {CaBytes} bytes)",
                client is not null, !string.IsNullOrWhiteSpace(dm.DiscordId), dm.CerBytes.Length);
            return false;
        }

        try {
            byte[] profile = MobileConfig.BuildCaProfile(dm.CerBytes, dm.DiscordId);
            var result = await client.SendAsync(dm.DiscordId, new DmMessage {
                Content = BuildMessage(dm),
                Files = [new DmFile(ProfileFile, profile, "application/x-apple-aspen-config")]
            }, ct);
            if (!result.IsSent)
                logger.LogWarning("capture setup DM to {DiscordId}: {Status} (http {Http}, discord {Code})",
                    dm.DiscordId, result.Status, result.HttpStatus, result.DiscordCode);
            return result.IsSent;
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
}
