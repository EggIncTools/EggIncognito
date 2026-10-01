using EggIncognito.Data.Services;
using EggIncognito.Models.Events;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions.Events;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Predictions;

public sealed class EventPredictor(EggIncognitoDbContext db, EventDataVersion version, EventPredictionCache cache,
    TimeProvider time) {
    public async Task<EventPredictionSet> GetAsync(
        int horizonDays = 28, double? asOf = null, CancellationToken ct = default) {
        var rows = await RowsAsync(ct);
        double at = asOf ?? UnixSeconds.FromTime(time.GetUtcNow());
        return new EventPredictionSet(at, Predict(rows, at, horizonDays));
    }

    public async Task<EventModelResponse> GetModelAsync(CancellationToken ct = default) {
        var rows = await RowsAsync(ct);
        double asOf = UnixSeconds.FromTime(time.GetUtcNow());
        var model = Train(rows, asOf);
        var rules = model.Rules.Select(r => new EventRuleSummary(
            r.Kind, r.Key, r.Type, r.Ultra, r.Evidence.Observed, r.Evidence.Expected, r.Evidence.Fill,
            r.PeriodDays, r.Evidence.LastStart, r.Evidence.Summary)).ToList();
        return new EventModelResponse(model.TrainedAt, model.WindowDays, rules, EventBacktest.Sweep(rows, asOf));
    }

    public async Task<IReadOnlyList<EventRow>> RowsAsync(CancellationToken ct = default) {
        long v = version.Version;
        if (cache.TryGet(v, out var cached)) return cached;

        var rows = await db.GameEvents.AsNoTracking()
            .OrderBy(e => e.StartTime)
            .Select(e => new { e.EventType, e.Ultra, e.StartTime, e.EndTime })
            .ToListAsync(ct);
        var value = rows
            .Select(r => new EventRow(
                r.EventType, r.Ultra, UnixSeconds.FromTime(r.StartTime), UnixSeconds.FromTime(r.EndTime)))
            .ToList();
        cache.Set(v, value);
        return value;
    }

    public static EventModel Train(IReadOnlyList<EventRow> rows, double asOf) =>
        EventModelTrainer.Train(EventHistory.Build(rows, asOf), asOf);

    public static IReadOnlyList<EventPrediction> Predict(IReadOnlyList<EventRow> rows, double asOf, int horizonDays) {
        var history = EventHistory.Build(rows, asOf);
        return EventProjector.Project(EventModelTrainer.Train(history, asOf), history, asOf, horizonDays);
    }
}
