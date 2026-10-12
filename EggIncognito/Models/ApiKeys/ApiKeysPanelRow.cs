namespace EggIncognito.Models.ApiKeys;

public record ApiKeysPanelRow(long Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, long RequestCount, bool Revoked, IReadOnlyList<ApiKeyFeatureView>? Features = null);
