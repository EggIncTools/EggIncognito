namespace EggIncognito.Models.Coverage;

public sealed record CoverageSelection(string Family, string SpecName, string Level, int AfxLevel, string Rarity, CoverageCell Cell);
