using EggIncognito.Data.Services;
using EggIncognito.Models.Calendar;
using EggIncognito.Models.Events;
using EggIncognito.Services.Assets;
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

    public async Task<WatchResponse> GetWatchAsync(int horizonDays = 28, CancellationToken ct = default) {
        var rows = await RowsAsync(ct);
        double at = UnixSeconds.FromTime(time.GetUtcNow());
        return new WatchResponse(at, Watch(rows, Predict(rows, at, horizonDays), at));
    }

    public static IReadOnlyList<WatchEntry> Watch(
        IReadOnlyList<EventRow> rows, IReadOnlyList<EventPrediction> predictions, double asOf) {
        var last = rows.Where(r => r.Start < asOf)
            .GroupBy(r => (r.Type, r.Ultra))
            .ToDictionary(g => g.Key, g => g.MaxBy(r => r.Start));
        var next = new Dictionary<(string Type, bool Ultra), List<WatchForecast>>();
        foreach (var p in predictions.Where(p => p.PredictedStart >= asOf)) {
            if (p.Type is { } type) {
                Add(next, (type, p.Ultra), new WatchForecast(p.PredictedStart, p.PredictedEnd, p.Confidence, p.Rule));
                continue;
            }

            foreach (var c in p.Candidates) {
                Add(next, (c.Type, p.Ultra),
                    new WatchForecast(p.PredictedStart, p.PredictedEnd, p.Confidence * c.Probability, p.Rule));
            }
        }

        return [
            .. last.Keys.Union(next.Keys)
                .OrderBy(k => k.Ultra).ThenBy(k => k.Type, StringComparer.Ordinal)
                .Select(k => new WatchEntry(
                    $"{k.Type}:{(k.Ultra ? "ultra" : "std")}", EventTypeLabel(k.Type), k.Ultra,
                    EventPalette.IconUrl(k.Type, k.Ultra), EventPalette.BarStyle(k.Type, k.Ultra),
                    last.TryGetValue(k, out var row) ? row.Start : null,
                    last.TryGetValue(k, out row) ? row.End : null,
                    next.TryGetValue(k, out var list) ? [.. list.OrderBy(f => f.Start)] : []))
        ];
    }

    private static void Add<TKey>(Dictionary<TKey, List<WatchForecast>> map, TKey key, WatchForecast forecast)
        where TKey : notnull {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(forecast);
    }

    private static string EventTypeLabel(string type) =>
        string.Join(' ', type.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

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
