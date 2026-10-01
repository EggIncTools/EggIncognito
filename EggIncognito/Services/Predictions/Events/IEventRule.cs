using EggIncognito.Models.Events;

namespace EggIncognito.Services.Predictions.Events;

public interface IEventRule {
    EventRuleKind Kind { get; }

    string Key { get; }

    string? Type { get; }

    bool Ultra { get; }

    int PeriodDays { get; }

    int Rank { get; }

    RuleEvidence Evidence { get; }

    IEnumerable<DateOnly> Dates(double asOf, double horizonEnd);

    EventPrediction? Realize(DateOnly date, EventLedger ledger);
}
