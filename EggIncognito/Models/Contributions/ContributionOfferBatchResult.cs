namespace EggIncognito.Models.Contributions;

public sealed record ContributionOfferBatchResult(int Recorded, IReadOnlyList<long> Missing);
