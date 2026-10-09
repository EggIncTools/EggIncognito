namespace EggIncognito.Models.Coverage;

public sealed record CoverageFamilyInfo(string SpecName, string PluralName, int Order, IReadOnlyList<string> TierNames);
