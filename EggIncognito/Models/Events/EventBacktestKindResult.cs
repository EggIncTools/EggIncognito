namespace EggIncognito.Models.Events;

public sealed record EventBacktestKindResult(
    EventRuleKind Kind,
    int Predicted,
    int SlotHit,
    int TypeHit,
    int Top3Hit);
