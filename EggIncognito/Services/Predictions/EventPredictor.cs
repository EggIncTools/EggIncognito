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

    public const int MagnitudeWindowDays = 365;

    public static IReadOnlyList<WatchEntry> Watch(
        IReadOnlyList<EventRow> rows, IReadOnlyList<EventPrediction> predictions, double asOf) {
        var past = rows.Where(r => r.Start < asOf).ToList();
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

        double windowStart = asOf - MagnitudeWindowDays * EventHistory.Day;
        var entries = new List<WatchEntry>();
        foreach (var kind in past.Select(r => (r.Type, r.Ultra)).Union(next.Keys).OrderBy(k => k.Ultra).ThenBy(k => k.Type, StringComparer.Ordinal)) {
            var history = past.Where(r => r.Type == kind.Type && r.Ultra == kind.Ultra).ToList();
            var recent = history.Where(r => r.Start >= windowStart).ToList();
            var forecasts = next.TryGetValue(kind, out var list) ? list.OrderBy(f => f.Start).ToList() : [];
            var magnitudes = recent.Select(r => r.Multiplier).Distinct().OrderBy(m => m).ToList();
            if (magnitudes.Count == 0) magnitudes = [history.Count > 0 ? history[^1].Multiplier : 0];
            foreach (var magnitude in magnitudes) {
                var runs = history.Where(r => r.Multiplier == magnitude).ToList();
                var lastRun = runs.Count > 0 ? runs.MaxBy(r => r.Start) : (EventRow?)null;
                double share = magnitudes.Count == 1 || recent.Count == 0
                    ? 1
                    : (double)recent.Count(r => r.Multiplier == magnitude) / recent.Count;
                string scalar = EventMagnitude.Describe(magnitude);
                entries.Add(new WatchEntry(
                    FormattableString.Invariant($"{kind.Type}:{(kind.Ultra ? "ultra" : "std")}:{magnitude:0.###}"),
                    scalar.Length > 0 ? $"{EventTypeLabel(kind.Type)} {scalar}" : EventTypeLabel(kind.Type), kind.Ultra,
                    EventPalette.IconUrl(kind.Type, kind.Ultra), EventPalette.BarStyle(kind.Type, kind.Ultra),
                    lastRun?.Start, lastRun?.End,
                    [.. forecasts.Select(f => f with { Confidence = f.Confidence * share })]));
            }
        }

        return entries;
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
            .Select(e => new { e.EventType, e.Ultra, e.StartTime, e.EndTime, e.Multiplier })
            .ToListAsync(ct);
        var value = rows
            .Select(r => new EventRow(
                r.EventType, r.Ultra, UnixSeconds.FromTime(r.StartTime), UnixSeconds.FromTime(r.EndTime), r.Multiplier))
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
