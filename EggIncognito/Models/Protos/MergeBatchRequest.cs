namespace EggIncognito.Models.Protos;

public sealed record MergeBatchRequest(IReadOnlyList<MergeRequest>? Groups);
