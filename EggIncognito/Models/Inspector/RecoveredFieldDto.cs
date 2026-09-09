namespace EggIncognito.Models.Inspector;

public sealed record RecoveredFieldDto(int Field, string? ResolvedName, string Wire, string Value, bool Bad);
