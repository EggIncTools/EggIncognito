namespace EggIncognito.Models.Contributions;

public sealed record ContributionTallyDto(
    Guid ContributorUserId,
    string? ContributorName,
    string Kind,
    int Submitted,
    DateTimeOffset Oldest);
