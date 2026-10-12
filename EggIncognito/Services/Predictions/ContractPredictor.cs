using System.Globalization;
using EggIncognito.Data.Services;
using EggIncognito.Models.Calendar;
using EggIncognito.Models.Contracts;
using EggIncognito.Services.Assets;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions.Contracts;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Predictions;

public sealed class ContractPredictor(
    EggIncognitoDbContext db, ContractDataVersion version, ContractPredictionCache cache,
    ILogger<ContractPredictor> logger, TimeProvider time) {
    private const int SnapHorizon = 12;
    private const double MinGridConformance = 0.9;

    public async Task<ContractPredictionResponse> GetSlotsAsync(int horizonSlots, CancellationToken ct = default) {
        var samples = await SamplesAsync(ct);
        var now = time.GetUtcNow();
        double nowSeconds = UnixSeconds.FromTime(now);
        var model = ContractModel.Train(samples, nowSeconds);
        WarnLowConformance(model);
        return new ContractPredictionResponse(nowSeconds, model.Slots(now, horizonSlots));
    }

    public async Task<ContractNextEstimate?> GetContractAsync(string contractId, CancellationToken ct = default) {
        var samples = await SamplesAsync(ct);
        double now = UnixSeconds.FromTime(time.GetUtcNow());
        var model = ContractModel.Train(samples, now);
        foreach (var kind in ReleaseGrid.AllKinds) {
            var pool = model.Pools[kind];
            var candidate = pool.Candidates.FirstOrDefault(c => c.ContractId == contractId);
            if (candidate is null) continue;
            double? estimate = pool.GapSeconds is { } gap
                ? SnapToSlot(Math.Max(candidate.LastReleased + gap, now), kind, model.Grid)
                : null;
            return new ContractNextEstimate(
                candidate.ContractId, candidate.Name, candidate.LastReleased,
                estimate, kind, candidate.Releases, pool.GapSamples);
        }

        return null;
    }

    public async Task<WatchResponse> GetWatchAsync(CancellationToken ct = default) {
        var samples = await SamplesAsync(ct);
        double now = UnixSeconds.FromTime(time.GetUtcNow());
        return new WatchResponse(now, Watch(samples, ContractModel.Train(samples, now), now));
    }

    public async Task<WatchResponse> GetColleggtibleWatchAsync(CancellationToken ct = default) {
        var samples = await SamplesAsync(ct);
        double now = UnixSeconds.FromTime(time.GetUtcNow());
        return new WatchResponse(now, ColleggtibleWatch(samples, ContractModel.Train(samples, now), now));
    }

    internal const int WatchWeeks = 52;

    private static Dictionary<string, List<WatchForecast>> Forecasts(ContractModel model, double now) {
        var forecasts = new Dictionary<string, List<WatchForecast>>(StringComparer.Ordinal);
        foreach (var slot in model.Slots(UnixSeconds.ToTime(now), WatchWeeks * ReleaseGrid.AllKinds.Length)) {
            foreach (var c in slot.Candidates) {
                if (!forecasts.TryGetValue(c.ContractId, out var list)) forecasts[c.ContractId] = list = [];
                list.Add(new WatchForecast(slot.SlotTime, slot.SlotTime + slot.LengthSeconds, c.Probability, Label(slot.Kind)));
            }
        }

        return forecasts;
    }

    internal static IReadOnlyList<WatchEntry> ColleggtibleWatch(
        IReadOnlyList<ContractReleaseSample> samples, ContractModel model, double now) {
        var forecasts = Forecasts(model, now);
        var entries = new List<WatchEntry>();
        foreach (var egg in samples.Where(s => s.CustomEggId is not null).GroupBy(s => s.CustomEggId!, StringComparer.Ordinal)) {
            var past = egg.Where(s => s.Start < now).ToList();
            if (past.Count == 0) continue;
            var last = past.MaxBy(s => s.Start);
            var names = egg.GroupBy(s => s.ContractId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.MaxBy(s => s.Start).Name, StringComparer.Ordinal);
            var next = names.Keys
                .SelectMany(id => (forecasts.GetValueOrDefault(id) ?? []).Select(f => (Id: id, Forecast: f)))
                .GroupBy(x => (x.Forecast.Start, x.Forecast.Rule))
                .Select(g => new WatchForecast(
                    g.Key.Start, g.Max(x => x.Forecast.End), Math.Min(1, g.Sum(x => x.Forecast.Confidence)), g.Key.Rule,
                    string.Join(", ", g.OrderByDescending(x => x.Forecast.Confidence).Select(x => names[x.Id]))))
                .OrderBy(f => f.Start)
                .ToList();
            entries.Add(new WatchEntry(
                egg.Key, EggIcons.Label(last.Egg, egg.Key), false, EggIcons.Url(last.Egg, egg.Key),
                ContractPalette.BarStyle(false, last.Leggacy, last.ProphecyEggs),
                last.Start, last.Start + last.LengthSeconds, next, last.Name));
        }

        return [.. entries.OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase)];
    }

    internal static IReadOnlyList<WatchEntry> Watch(
        IReadOnlyList<ContractReleaseSample> samples, ContractModel model, double now) {
        var last = samples.Where(s => s.Start < now).GroupBy(s => s.ContractId)
            .ToDictionary(g => g.Key, g => g.MaxBy(s => s.Start), StringComparer.Ordinal);
        var forecasts = Forecasts(model, now);
        var entries = new List<WatchEntry>();
        foreach (var c in model.Pools.Values.SelectMany(p => p.Candidates).DistinctBy(c => c.ContractId)) {
            if (!last.TryGetValue(c.ContractId, out var row)) continue;
            entries.Add(new WatchEntry(
                c.ContractId, c.Name, false, EggIcons.Url(row.Egg, row.CustomEggId),
                ContractPalette.BarStyle(false, row.Leggacy, row.ProphecyEggs),
                row.Start, row.Start + row.LengthSeconds, forecasts.GetValueOrDefault(c.ContractId) ?? []));
        }

        return [.. entries.OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase)];
    }

    private static string Label(ContractSlotKind kind) => kind switch {
        ContractSlotKind.NewContract => "Monday new contract",
        ContractSlotKind.Leggacy => "Wednesday leggacy",
        ContractSlotKind.PeLeggacy => "Friday PE leggacy",
        _ => "Friday PE leggacy, ultra only"
    };

    public async Task<ContractModelResponse> GetModelAsync(CancellationToken ct = default) {
        var samples = await SamplesAsync(ct);
        double now = UnixSeconds.FromTime(time.GetUtcNow());
        var model = ContractModel.Train(samples, now);
        var grid = model.Grid.Slots.Select(s => new ContractGridSummary(
            s.Kind, s.Weekday, s.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
            s.Evidence.Observed, s.Evidence.Expected, s.Evidence.Summary)).ToList();
        var pools = ReleaseGrid.AllKinds.Select(k => model.Pools[k]).Select(p => new ContractPoolSummary(
            p.Kind, p.Candidates.Count, p.GapSeconds / 86400d, p.GapSamples, p.LengthSeconds / 86400d,
            p.Evidence.Summary)).ToList();
        return new ContractModelResponse(model.TrainedAt, grid, pools, ContractBacktest.Sweep(samples, now));
    }

    public async Task<IReadOnlyList<ContractReleaseSample>> SamplesAsync(CancellationToken ct = default) {
        long v = version.Version;
        if (cache.TryGet(v, out var cached)) return cached;

        var rows = await db.ContractReleases.AsNoTracking()
            .Select(r => new {
                r.ContractId,
                r.Name,
                r.StartTime,
                r.LengthSeconds,
                r.Leggacy,
                r.ProphecyEggs,
                r.UltraOnly,
                r.Egg,
                r.CustomEggId
            })
            .ToListAsync(ct);
        var samples = rows
            .Select(r => new ContractReleaseSample(
                r.ContractId, r.Name, UnixSeconds.FromTime(r.StartTime), r.LengthSeconds, r.Leggacy, r.ProphecyEggs,
                r.UltraOnly, r.Egg, r.CustomEggId))
            .OrderBy(s => s.Start)
            .ToList();
        cache.Set(v, samples);
        return samples;
    }

    private void WarnLowConformance(ContractModel model) {
        foreach (var slot in model.Grid.Slots) {
            if (slot.Evidence.Expected == 0 || slot.Evidence.Fill >= MinGridConformance) continue;
            logger.LogWarning(
                "Contract release grid conformance for {Kind} is {Share:P0} over {Count} releases in the last 180 days, below {Minimum:P0}",
                slot.Kind, slot.Evidence.Fill, slot.Evidence.Expected, MinGridConformance);
        }
    }

    internal static double SnapToSlot(double estimate, ContractSlotKind pool, ReleaseGrid grid) {
        if (!UnixSeconds.IsValid(estimate)) return estimate;
        var from = UnixSeconds.ToTime(estimate).AddSeconds(-1);
        foreach (var (slotTime, slot) in grid.Next(from, SnapHorizon)) {
            if (slot.Kind == pool && slotTime >= estimate) return slotTime;
        }

        return estimate;
    }
}
