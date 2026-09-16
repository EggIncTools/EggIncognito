namespace EggIncognito.Models.Contributions;

public sealed record ContributionOfferBatchRequest(string DeviceId, IReadOnlyList<long>? FlowIds) {
    public IReadOnlyList<long> Ids => FlowIds ?? [];
}
