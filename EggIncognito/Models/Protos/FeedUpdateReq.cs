using System.Text.Json;

namespace EggIncognito.Models.Protos;

public sealed record FeedUpdateReq(bool? Active, string? MessageTemplate, JsonElement? Filter = null, string? Label = null);
