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
        return new ContractPredictionResponse(nowSeconds, model.Slots(now, horizonSlots, nowSeconds));
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

    internal const int WatchHorizonSlots = 12;

    internal static IReadOnlyList<WatchEntry> Watch(
        IReadOnlyList<ContractReleaseSample> samples, ContractModel model, double now) {
        var last = samples.Where(s => s.Start < now).GroupBy(s => s.ContractId)
            .ToDictionary(g => g.Key, g => g.MaxBy(s => s.Start), StringComparer.Ordinal);
        var rates = ContractBacktest.Sweep(samples, now).Totals.ToDictionary(t => t.Kind);
        var next = new Dictionary<string, List<WatchForecast>>(StringComparer.Ordinal);
        foreach (var slot in model.Slots(UnixSeconds.ToTime(now), WatchHorizonSlots, now)) {
            var kindRate = rates[slot.Kind];
            for (int rank = 0; rank < slot.Candidates.Count; rank++) {
                var c = slot.Candidates[rank];
                if (!next.TryGetValue(c.ContractId, out var list)) next[c.ContractId] = list = [];
                list.Add(new WatchForecast(
                    slot.SlotTime, slot.SlotTime + slot.LengthSeconds, RankConfidence(kindRate, rank, slot.Candidates.Count),
                    $"{Label(slot.Kind)}, queue position {rank + 1} of {slot.Candidates.Count}"));
            }
        }

        var entries = new List<WatchEntry>();
        foreach (var c in ReleaseGrid.AllKinds.SelectMany(k => model.Pools[k].Candidates)) {
            if (!last.TryGetValue(c.ContractId, out var row)) continue;
            entries.Add(new WatchEntry(
                c.ContractId, c.Name, row.UltraOnly, EggIcons.Url(row.Egg, row.CustomEggId),
                ContractPalette.BarStyle(row.UltraOnly, row.Leggacy, row.ProphecyEggs),
                row.Start, row.Start + row.LengthSeconds,
                next.TryGetValue(c.ContractId, out var list) ? [.. list.OrderBy(f => f.Start)] : []));
        }

        return [.. entries.OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase)];
    }

    internal static double RankConfidence(ContractBacktestKindResult rate, int rank, int shown) {
        if (rate.SlotHit == 0) return shown == 0 ? 0 : 1d / shown;
        double top1 = (double)rate.Top1Hit / rate.SlotHit;
        if (rank == 0) return top1;
        double rest = Math.Max(0, (double)(rate.Top5Hit - rate.Top1Hit) / rate.SlotHit);
        return shown > 1 ? rest / (shown - 1) : rest;
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
