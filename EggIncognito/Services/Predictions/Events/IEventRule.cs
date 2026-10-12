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

    double Duration(string type);

    IReadOnlyList<EventCandidate> Odds(DateOnly date, EventLedger ledger);
}
