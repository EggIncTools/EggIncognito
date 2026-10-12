using System.Text.Json;

namespace EggIncognito.Models.Protos;

public sealed record FeedPreviewReq(string? EventKind, JsonElement? Filter, string? MessageTemplate);
