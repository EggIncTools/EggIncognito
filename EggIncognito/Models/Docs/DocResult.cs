namespace EggIncognito.Models.Docs;

public sealed record DocResult(string? BodyMd, DateTimeOffset? UpdatedAt = null, Guid? Owner = null);
