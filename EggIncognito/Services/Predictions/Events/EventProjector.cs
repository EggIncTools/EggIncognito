using EggIncognito.Models.Events;

namespace EggIncognito.Services.Predictions.Events;

public static class EventProjector {
    public const int MinHorizonDays = 1;
    public const int MaxHorizonDays = 90;

    public static IReadOnlyList<EventPrediction> Project(
        EventModel model, EventHistory history, double asOf, int horizonDays) {
        int horizon = Math.Clamp(horizonDays, MinHorizonDays, MaxHorizonDays);
        double horizonEnd = asOf + horizon * EventHistory.Day;
        var ledger = EventLedger.From(history);
        var plans = model.Rules
            .SelectMany(rule => rule.Dates(asOf, horizonEnd).Select(date => (Rule: rule, Date: date)))
            .OrderBy(p => NoonEastern.SlotTime(p.Date))
            .ThenBy(p => p.Rule.Rank)
            .ThenBy(p => p.Rule.Key, StringComparer.Ordinal)
            .ToList();
        var used = new HashSet<(string Key, DateOnly Date)>();
        var predictions = new List<EventPrediction>(plans.Count);
        foreach (var (rule, date) in plans) {
            if (!used.Add((rule.Key, date))) continue;
            if (rule.Realize(date, ledger) is { } prediction) predictions.Add(prediction);
        }

        return predictions;
    }
}
