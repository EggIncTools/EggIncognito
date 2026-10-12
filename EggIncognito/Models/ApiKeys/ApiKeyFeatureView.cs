namespace EggIncognito.Models.ApiKeys;

public sealed record ApiKeyFeatureView(string Feature, string State, DateTimeOffset RequestedAt, DateTimeOffset? DecidedAt);
