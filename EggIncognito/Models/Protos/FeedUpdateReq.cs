namespace EggIncognito.Models.Protos;

public sealed record FeedUpdateReq(
    string[]? Platforms, string? Trigger, bool? Active, string? MessageTemplate, string[]? Filters = null,
    string? Label = null);
