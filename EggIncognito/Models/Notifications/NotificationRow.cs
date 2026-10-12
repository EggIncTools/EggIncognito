using System.Text.Json;

namespace EggIncognito.Models.Notifications;

public record NotificationRow(
    int Id,
    string? Label,
    string EventKind,
    JsonElement Filter,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastDeliveryAt,
    int FailCount,
    string? MessageTemplate,
    string UrlMasked);
