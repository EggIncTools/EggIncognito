namespace EggIncognito.Services.Predictions.Events;

public static class EventModelTrainer {
    public static EventModel Train(EventHistory history, double asOf, EventStrategy strategy) {
        if (history.Empty) return new EventModel(asOf, EventHistory.WindowDays, [], strategy, history);
        var rules = new List<IEventRule>();
        var lanes = WeekdayLaneRule.Fit(history);
        var claimed = lanes.Select(l => (l.Type!, l.Weekday)).ToHashSet();
        rules.AddRange(lanes);
        var alternations = AlternatingLaneRule.Fit(history, claimed);
        rules.AddRange(alternations);
        rules.AddRange(PeriodicLaneRule.Fit(history, claimed, alternations));
        rules.AddRange(RotatingPoolRule.Fit(history, claimed, strategy));
        if (UltraCadenceRule.Fit(history, strategy) is { } ultra) rules.Add(ultra);
        return new EventModel(asOf, EventHistory.WindowDays, rules, strategy, history);
    }
}
