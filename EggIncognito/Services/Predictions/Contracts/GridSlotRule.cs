using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions.Contracts;

public sealed record GridSlotRule(ContractSlotKind Kind, DayOfWeek Weekday, TimeOnly Time, RuleEvidence Evidence);
