namespace EggIncognito.Models.Contributions;

public sealed record ContributionPendingRowDto(
    long Id,
    Guid ContributorUserId,
    string? ContributorName,
    string Kind,
    string Summary,
    string Payload,
    string? ClientVersion,
    DateTimeOffset RecordedAt,
    DateTimeOffset? SubmittedAt);
