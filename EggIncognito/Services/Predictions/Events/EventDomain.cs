using EggIncognito.Models.Events;

namespace EggIncognito.Services.Predictions.Events;

public sealed class EventDomain : IPredictionDomain<EventRow, EventStrategy, EventModel> {
    public string Name => "events";

    public IReadOnlyList<EventStrategy> Strategies => EventStrategy.Grid;

    public IReadOnlyList<PredictionSlot> Slots(IReadOnlyList<EventRow> samples, double from, double to) {
        var byKey = new Dictionary<(bool Ultra, DateOnly Date), HashSet<string>>();
        foreach (var row in samples.Where(r => r.Start >= from && r.Start < to)) {
            var date = NoonEastern.LocalDate(row.Start);
            var key = (row.Ultra, date);
            if (!byKey.TryGetValue(key, out var types)) byKey[key] = types = [];
            types.Add(row.Type);
        }

        if (byKey.Count == 0) return [];
        var ownership = OwnershipByPair(Train(samples, to, EventStrategy.Default));
        return byKey
            .Select(kv => new PredictionSlot(NoonEastern.SlotTime(kv.Key.Date), KindOf(kv.Key.Ultra, kv.Key.Date, kv.Value, ownership)))
            .OrderBy(s => s.Time)
            .ToList();
    }

    public EventModel Train(IReadOnlyList<EventRow> samples, double asOf, EventStrategy strategy) =>
        EventModelTrainer.Train(EventHistory.Build(samples, asOf), asOf, strategy);

    public IReadOnlyList<PredictionCandidate> Odds(EventModel model, PredictionSlot slot) {
        var date = NoonEastern.LocalDate(slot.Time);
        var ledger = EventLedger.From(model.History);
        var totals = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var rule in model.Rules.Where(r => Owns(r, date))) {
            foreach (var candidate in rule.Odds(date, ledger)) {
                totals[candidate.Type] = Math.Min(1, totals.GetValueOrDefault(candidate.Type) + candidate.Probability);
            }
        }

        return totals
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new PredictionCandidate(kv.Key, kv.Value))
            .ToList();
    }

    public IReadOnlySet<string> Actual(IReadOnlyList<EventRow> samples, PredictionSlot slot) {
        bool ultra = string.Equals(slot.Kind, nameof(EventRuleKind.Ultra), StringComparison.Ordinal);
        var date = NoonEastern.LocalDate(slot.Time);
        return samples
            .Where(r => r.Ultra == ultra && NoonEastern.LocalDate(r.Start) == date)
            .Select(r => r.Type)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool Owns(IEventRule rule, DateOnly date) {
        double at = NoonEastern.SlotTime(date);
        return rule.Dates(at, at + 1).Any(d => d == date);
    }

    private static Dictionary<(string Type, DayOfWeek Weekday), string> OwnershipByPair(EventModel model) {
        var map = new Dictionary<(string Type, DayOfWeek Weekday), string>();
        foreach (var rule in model.Rules) {
            switch (rule) {
                case WeekdayLaneRule w:
                    map[(w.Type!, w.Weekday)] = nameof(EventRuleKind.WeekdayLane);
                    break;
                case AlternatingLaneRule a:
                    foreach (string member in a.Members) map[(member, a.Weekday)] = nameof(EventRuleKind.Alternating);
                    break;
                case PeriodicLaneRule p:
                    map[(p.Type!, p.Weekday)] = nameof(EventRuleKind.Periodic);
                    break;
                case RotatingPoolRule r:
                    foreach (string type in r.Types) map.TryAdd((type, r.Weekday), nameof(EventRuleKind.Pool));
                    break;
            }
        }

        return map;
    }

    private static string KindOf(
        bool ultra, DateOnly date, IReadOnlySet<string> types, IReadOnlyDictionary<(string Type, DayOfWeek Weekday), string> ownership) {
        if (ultra) return nameof(EventRuleKind.Ultra);
        var kinds = types
            .Select(t => ownership.GetValueOrDefault((t, date.DayOfWeek), nameof(EventRuleKind.Pool)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return kinds.Count == 1 ? kinds[0] : nameof(EventRuleKind.Pool);
    }
}
