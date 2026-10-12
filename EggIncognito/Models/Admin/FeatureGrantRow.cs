namespace EggIncognito.Models.Admin;

public sealed record FeatureGrantRow(int Id, int KeyId, string KeyName, string KeyPrefix, string OwnerUserId,
    string Feature, string State, string? Note, DateTimeOffset RequestedAt, DateTimeOffset? DecidedAt,
    string? DecidedBy);
