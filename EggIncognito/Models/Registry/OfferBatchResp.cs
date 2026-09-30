namespace EggIncognito.Models.Registry;

public sealed record OfferBatchResp(IReadOnlyList<OfferBatchItemResp>? Results);
