using EggIncognito.Models.Calendar;

namespace EggIncognito.Models.Events;

public sealed record EventCalendarItem(
    string Key,
    double Start,
    double End,
    GameEventDto? Event,
    EventPrediction? Prediction) : ICalendarSpan {
    public bool Ghost => Prediction is not null;

    public bool Ultra => Event?.Ultra ?? Prediction?.Ultra ?? false;

    public string? Type => Event?.Type ?? Prediction?.Type;

    public static EventCalendarItem Of(GameEventDto e) => new(
        FormattableString.Invariant($"{e.Id}@{e.StartTimestamp:0}"), e.StartTimestamp, e.EndTimestamp, e, null);

    public static EventCalendarItem Of(EventPrediction p) => new(
        FormattableString.Invariant($"pe:{p.Rule}:{p.Ultra}:{p.PredictedStart:0}"),
        p.PredictedStart, p.PredictedEnd, null, p);
}
