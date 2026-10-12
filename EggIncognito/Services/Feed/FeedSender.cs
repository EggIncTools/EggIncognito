using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EggIdentity.Bot;
using EggIdentity.Contract;
using EggIncognito.Data.Models;

namespace EggIncognito.Services.Feed;

public sealed record FeedSendResult(bool Ok, int? Code, bool Dead);

public sealed class FeedSender(IHttpClientFactory httpFactory, BotDmClientFactory dms) {
    public const string WebhookKind = "discord";
    public const string DmKind = "discord_dm";
    public const string DmTarget = "Discord DM";

    public static bool IsDm(FeedSubscription sub) => sub.Kind == DmKind;

    public static string Describe(FeedSubscription sub) => IsDm(sub) ? DmTarget : WebhookMask.Mask(sub.TargetUrl);

    public async Task<FeedSendResult> SendAsync(FeedSubscription sub, string body, CancellationToken ct) {
        if (IsDm(sub)) {
            var dm = await DmAsync(sub.TargetUrl, body, ct);
            return new FeedSendResult(dm.IsSent, dm.HttpStatus, dm.Status is DmSendStatus.CannotDm or DmSendStatus.UnknownUser);
        }

        var res = await httpFactory.CreateClient("discord-api")
            .PostAsync(sub.TargetUrl, new StringContent(body, Encoding.UTF8, "application/json"), ct);
        int code = (int)res.StatusCode;
        return new FeedSendResult(res.IsSuccessStatusCode, code, code is 404 or 410);
    }

    public async Task<DmSendResult> DmAsync(string discordId, string body, CancellationToken ct) =>
        dms.Create() is { } client
            ? await client.SendAsync(discordId, ToDm(body), ct)
            : new DmSendResult(DmSendStatus.Failed);

    public static DmMessage ToDm(string body) {
        JsonObject obj;
        try {
            obj = JsonNode.Parse(body) as JsonObject ?? [];
        } catch (JsonException) {
            return new DmMessage { Content = body };
        }

        return new DmMessage {
            Content = obj["content"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null,
            Embeds = obj["embeds"] is JsonArray embeds
                ? [.. embeds.OfType<JsonObject>().Select(e => (JsonObject)e.DeepClone())]
                : null
        };
    }

    public static string DmFailure(DmSendResult result) => result.Status switch {
        DmSendStatus.CannotDm => "Discord refused the DM: allow direct messages from server members",
        DmSendStatus.UnknownUser => "Discord does not know this account",
        DmSendStatus.RateLimited => "Discord rate limited the DM; try again shortly",
        _ => "the DM could not be sent"
    };
}
