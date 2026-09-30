namespace EggIncognito.Models.Protos;

public sealed record CheckBatchRequest(IReadOnlyList<CheckItem>? Items);
