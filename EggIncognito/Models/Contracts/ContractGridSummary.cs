namespace EggIncognito.Models.Contracts;

public sealed record ContractGridSummary(
    ContractSlotKind Kind,
    DayOfWeek Weekday,
    string Time,
    int Observed,
    int Expected,
    string Summary);
