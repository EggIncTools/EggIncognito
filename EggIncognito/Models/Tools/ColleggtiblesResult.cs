namespace EggIncognito.Models.Tools;

public sealed record ColleggtiblesResult(
    int Count,
    IReadOnlyList<ColleggtibleRow>? Eggs = null,
    IReadOnlyDictionary<string, string>? ContractEggMap = null,
    string? Error = null);
