using System.Text.Json;

namespace EggIncognito.Models.Protos;

public sealed record FeedCreateReq(
    string WebhookUrl,
    string? Label,
    string? MessageTemplate,
    string? EventKind = null,
    JsonElement? Filter = null);
