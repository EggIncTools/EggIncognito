namespace EggIncognito.Models.Registry;

public sealed record OfferBatchItemResp(int Index, string? Result, string? ProtoSha, string? Error);
