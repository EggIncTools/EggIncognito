namespace EggIncognito.Models.Coverage;

public sealed record CoverageTier(string Level, int AfxLevel, string Name, IReadOnlyList<CoverageCell> Cells);
