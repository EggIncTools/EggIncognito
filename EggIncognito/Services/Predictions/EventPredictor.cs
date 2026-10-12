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
        var next = new Dictionary<(string Type, bool Ultra), List<(WatchForecast Forecast, IReadOnlyList<EventMagnitudeOdds> Odds)>>();
        foreach (var p in predictions.Where(p => p.PredictedStart >= asOf)) {
            if (p.Type is { } type && p.Kind is not (EventRuleKind.Pool or EventRuleKind.Ultra)) {
                Add(next, (type, p.Ultra), (new WatchForecast(p.PredictedStart, p.PredictedEnd, p.Confidence, p.Rule), p.Magnitudes));
                continue;
            }

            foreach (var c in p.Candidates) {
                Add(next, (c.Type, p.Ultra),
                    (new WatchForecast(p.PredictedStart, p.PredictedEnd, p.Chance(c), p.Rule), c.Magnitudes));
            }
        }

        double windowStart = asOf - MagnitudeWindowDays * EventHistory.Day;
        var entries = new List<WatchEntry>();
        foreach (var kind in past.Select(r => (r.Type, r.Ultra)).Union(next.Keys).OrderBy(k => k.Ultra).ThenBy(k => k.Type, StringComparer.Ordinal)) {
            var history = past.Where(r => r.Type == kind.Type && r.Ultra == kind.Ultra).ToList();
            var recent = history.Where(r => r.Start >= windowStart).ToList();
            var forecasts = next.TryGetValue(kind, out var list) ? list.OrderBy(f => f.Forecast.Start).ToList() : [];
            var magnitudes = recent.Select(r => r.Multiplier).Distinct().OrderBy(m => m).ToList();
            if (magnitudes.Count == 0) magnitudes = [history.Count > 0 ? history[^1].Multiplier : 0];
            foreach (var magnitude in magnitudes) {
                var runs = history.Where(r => r.Multiplier == magnitude).ToList();
                var lastRun = runs.Count > 0 ? runs.MaxBy(r => r.Start) : (EventRow?)null;
                string scalar = EventMagnitude.Describe(magnitude);
                entries.Add(new WatchEntry(
                    FormattableString.Invariant($"{kind.Type}:{(kind.Ultra ? "ultra" : "std")}:{magnitude:0.###}"),
                    scalar.Length > 0 ? $"{EventTypeLabel(kind.Type)} {scalar}" : EventTypeLabel(kind.Type), kind.Ultra,
                    EventPalette.IconUrl(kind.Type, kind.Ultra), EventPalette.BarStyle(kind.Type, kind.Ultra),
                    lastRun?.Start, lastRun?.End,
                    [.. forecasts.Select(f => f.Forecast with { Confidence = f.Forecast.Confidence * MagnitudeShare(f.Odds, magnitude, magnitudes.Count) })]));
            }
        }

        return entries;
    }

    private static double MagnitudeShare(IReadOnlyList<EventMagnitudeOdds> odds, double magnitude, int kinds) =>
        kinds == 1 ? 1 : odds.FirstOrDefault(o => o.Multiplier == magnitude)?.Probability ?? 0;

    private static void Add<TKey, TValue>(Dictionary<TKey, List<TValue>> map, TKey key, TValue value)
        where TKey : notnull {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(value);
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
            .Where(r => !EggDay.Contains(r.Start))
            .ToList();
        cache.Set(v, value);
        return value;
    }

    public static EventModel Train(IReadOnlyList<EventRow> rows, double asOf) =>
        EventModelTrainer.Train(EventHistory.Build(rows, asOf), asOf);

    public static IReadOnlyList<EventPrediction> Predict(IReadOnlyList<EventRow> rows, double asOf, int horizonDays) {
        var history = EventHistory.Build(rows, asOf);
        var projected = EventProjector.Project(EventModelTrainer.Train(history, asOf), history, asOf, horizonDays);
        var models = new Dictionary<(string, bool), MagnitudeModel>();
        MagnitudeModel ModelOf(string type, bool ultra) {
            if (!models.TryGetValue((type, ultra), out var m)) {
                models[(type, ultra)] = m = MagnitudeModel.Build(rows.Where(r => r.Type == type && r.Ultra == ultra), asOf);
            }

            return m;
        }

        var result = new List<EventPrediction>(projected.Count);
        foreach (var p in projected.OrderBy(p => p.PredictedStart)) {
            var candidates = p.Candidates
                .Select(c => c with { Magnitudes = ModelOf(c.Type, p.Ultra).Step(p.Chance(c)) })
                .ToList();
            var own = p.Type is { } type ? candidates.FirstOrDefault(c => c.Type == type)?.Magnitudes : null;
            result.Add(p with {
                Magnitudes = own ?? (p.Type is { } t ? ModelOf(t, p.Ultra).Step(p.Confidence) : []),
                Candidates = candidates
            });
        }

        return result;
    }
}
