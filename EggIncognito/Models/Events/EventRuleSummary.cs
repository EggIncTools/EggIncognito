namespace EggIncognito.Models.Events;

public sealed record EventRuleSummary(
    EventRuleKind Kind,
    string Key,
    string? Type,
    bool Ultra,
    int Observed,
    int Expected,
    double Fill,
    int PeriodDays,
    double LastStart,
    string Summary);
