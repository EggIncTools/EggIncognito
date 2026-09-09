namespace EggIncognito.Models.Inspector;

public sealed record WireRecovery(int AlignedAt, int SkippedBytes, List<RecoveredFieldDto>? Fields);
