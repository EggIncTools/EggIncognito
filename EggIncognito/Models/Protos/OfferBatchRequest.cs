namespace EggIncognito.Models.Protos;

public sealed record OfferBatchRequest(IReadOnlyList<OfferItem>? Items);
