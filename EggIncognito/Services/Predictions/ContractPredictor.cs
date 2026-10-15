using System.Globalization;
using EggIncognito.Data.Services;
using EggIncognito.Models.Calendar;
using EggIncognito.Models.Contracts;
using EggIncognito.Models.Predictions;
using EggIncognito.Services.Assets;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions.Contracts;
using EggIncognito.Services.Predictions.Training;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Predictions;

public sealed class ContractPredictor(
    EggIncognitoDbContext db, ContractDataVersion version, ContractPredictionCache cache, PredictionTrainer trainer,
    ILogger<ContractPredictor> logger, TimeProvider time) {
    public async Task<ContractPredictionResponse> GetSlotsAsync(int horizonSlots, CancellationToken ct = default) {
        var entry = await TunedAsync(ct);
        var now = time.GetUtcNow();
        WarnLowConformance(entry.Tuned.Model);
        double nowSeconds = UnixSeconds.FromTime(now);
        var slots = entry.Slots.Where(s => s.SlotTime >= nowSeconds).Take(horizonSlots).ToList();
        return new ContractPredictionResponse(nowSeconds, slots);
    }

    public async Task<ContractNextEstimate?> GetContractAsync(string contractId, CancellationToken ct = default) {
        var model = (await TunedAsync(ct)).Tuned.Model;
        double now = UnixSeconds.FromTime(time.GetUtcNow());
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

    public async Task<WatchResponse> GetWatchAsync(int? horizonWeeks = null, CancellationToken ct = default) {
        var entry = await TunedAsync(ct);
        int step = PredictionStepPicker.Pick(entry.Watches.Keys, horizonWeeks);
        return new WatchResponse(entry.ComputedAt, entry.Watches[step]);
    }

    public async Task<WatchResponse> GetColleggtibleWatchAsync(int? horizonWeeks = null, CancellationToken ct = default) {
        var entry = await TunedAsync(ct);
        if (entry.ColleggtibleWatches is not { } watches)
            throw new PredictionsNotReadyException(PredictionDomainKey.Colleggtibles, trainer.Status(PredictionDomainKey.Colleggtibles));
        int step = PredictionStepPicker.Pick(watches.Keys, horizonWeeks);
        return new WatchResponse(entry.ComputedAt, watches[step]);
    }

    public async Task<NextContractsResponse> GetNextAsync(CancellationToken ct = default) {
        var entry = await TunedAsync(ct);
        return entry.Next
               ?? throw new PredictionsNotReadyException(PredictionDomainKey.Contracts, trainer.Status(PredictionDomainKey.Contracts));
    }

    internal static NextContractsResponse BuildNext(
        IReadOnlyList<ContractReleaseSample> samples, IReadOnlyList<ContractSlotPrediction> slots,
        TunedModel<ColleggtibleStrategy, ColleggtibleModel>? colleggtibles, double now) {
        NextPick? Pick(ContractSlotKind kind) {
            var slot = slots.Where(s => s.SlotTime >= now && s.Kind == kind).OrderBy(s => s.SlotTime).FirstOrDefault();
            var top = slot?.Candidates.OrderByDescending(c => c.Probability).FirstOrDefault();
            if (slot is null || top is null) return null;
            var rows = samples.Where(s => s.ContractId == top.ContractId).ToList();
            var latest = rows.Count > 0 ? rows.MaxBy(s => s.Start) : default;
            string? customEggId = rows.Count > 0 ? latest.CustomEggId : null;
            string? iconUrl = rows.Count > 0 ? EggIcons.Url(latest.Egg, latest.CustomEggId) : null;
            return new NextPick(top.ContractId, top.Name, slot.SlotTime, top.Probability, customEggId, iconUrl);
        }

        var colleggtiblePicks = new List<NextPick>();
        if (colleggtibles is { } tunedColleggtibles) {
            foreach (var kind in ContractDomain.PredictedKinds) {
                var slot = slots.Where(s => s.SlotTime >= now && s.Kind == kind).OrderBy(s => s.SlotTime).FirstOrDefault();
                if (slot is null) continue;
                var top = tunedColleggtibles.Model.Odds(new PredictionSlot(slot.SlotTime, kind.ToString()))
                    .OrderByDescending(c => c.Probability).FirstOrDefault();
                if (top is not null) {
                    colleggtiblePicks.Add(new NextPick(
                        top.Key, EggIcons.Label(0, top.Key), slot.SlotTime, top.Probability, top.Key, EggIcons.Url(0, top.Key)));
                }
            }
        }

        return new NextContractsResponse(
            now, Pick(ContractSlotKind.Leggacy), Pick(ContractSlotKind.PeLeggacy), Pick(ContractSlotKind.PeLeggacyUltra),
            colleggtiblePicks);
    }

    private static Dictionary<string, List<WatchForecast>> Forecasts(ContractModel model, double now, int horizonWeeks) {
        var forecasts = new Dictionary<string, List<WatchForecast>>(StringComparer.Ordinal);
        foreach (var slot in model.Slots(UnixSeconds.ToTime(now), horizonWeeks * ReleaseGrid.AllKinds.Length)) {
            foreach (var c in slot.Candidates) {
                if (!forecasts.TryGetValue(c.ContractId, out var list)) forecasts[c.ContractId] = list = [];
                list.Add(new WatchForecast(slot.SlotTime, slot.SlotTime + slot.LengthSeconds, c.Probability, Label(slot.Kind)));
            }
        }

        return forecasts;
    }

    internal static IReadOnlyList<WatchEntry> ColleggtibleWatch(
        IReadOnlyList<ContractReleaseSample> samples, ColleggtibleModel model, double now, int horizonWeeks) {
        var grid = ReleaseGrid.Fit(samples, now);
        var forecasts = new Dictionary<string, List<WatchForecast>>(StringComparer.Ordinal);
        foreach (var (time, gridSlot) in grid.Next(UnixSeconds.ToTime(now), horizonWeeks * ContractDomain.PredictedKinds.Length)) {
            if (!ContractDomain.PredictedKinds.Contains(gridSlot.Kind)) continue;
            var slot = new PredictionSlot(time, gridSlot.Kind.ToString());
            double length = model.Contracts?.Pools[gridSlot.Kind].LengthSeconds ?? 7 * 86400d;
            foreach (var candidate in model.Odds(slot)) {
                if (!forecasts.TryGetValue(candidate.Key, out var list)) forecasts[candidate.Key] = list = [];
                list.Add(new WatchForecast(time, time + length, candidate.Probability, Label(gridSlot.Kind)));
            }
        }

        var entries = new List<WatchEntry>();
        foreach (var egg in samples.Where(s => s.CustomEggId is not null).GroupBy(s => s.CustomEggId!, StringComparer.Ordinal)) {
            var past = egg.Where(s => s.Start < now).ToList();
            if (past.Count == 0) continue;
            var last = past.MaxBy(s => s.Start);
            var next = forecasts.GetValueOrDefault(egg.Key) ?? [];
            entries.Add(new WatchEntry(
                egg.Key, EggIcons.Label(last.Egg, egg.Key), false, EggIcons.Url(last.Egg, egg.Key),
                ContractPalette.BarStyle(false, last.Leggacy, last.ProphecyEggs),
                last.Start, last.Start + last.LengthSeconds, next, last.Name,
                next.Count == 0 ? "Not due within the current watch horizon." : null));
        }

        return [.. entries.OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase)];
    }

    private static string NoNextReason(ContractModel model, IEnumerable<string> contractIds, double now) {
        var ids = contractIds.ToHashSet(StringComparer.Ordinal);
        bool eligible = model.Pools.Values.Any(p =>
            OldestFirstPools.Eligible(p, now, model.Strategy, model.RankFit.NeverRerun).Any(c => ids.Contains(c.ContractId)));
        return eligible
            ? "Ran too recently. Leggacies rerun oldest first, so it is not due within a year."
            : "No rerun in far longer than usual. It looks retired.";
    }

    internal static IReadOnlyList<WatchEntry> Watch(
        IReadOnlyList<ContractReleaseSample> samples, ContractModel model, double now, int horizonWeeks) {
        var last = samples.Where(s => s.Start < now).GroupBy(s => s.ContractId)
            .ToDictionary(g => g.Key, g => g.MaxBy(s => s.Start), StringComparer.Ordinal);
        var forecasts = Forecasts(model, now, horizonWeeks);
        var entries = new List<WatchEntry>();
        foreach (var c in model.Pools.Values.SelectMany(p => p.Candidates).DistinctBy(c => c.ContractId)) {
            if (!last.TryGetValue(c.ContractId, out var row)) continue;
            var next = forecasts.GetValueOrDefault(c.ContractId) ?? [];
            entries.Add(new WatchEntry(
                c.ContractId, c.Name, false, EggIcons.Url(row.Egg, row.CustomEggId),
                ContractPalette.BarStyle(false, row.Leggacy, row.ProphecyEggs),
                row.Start, row.Start + row.LengthSeconds, next,
                NoNextReason: next.Count == 0 ? NoNextReason(model, [c.ContractId], now) : null));
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
        var entry = await TunedAsync(ct);
        var model = entry.Tuned.Model;
        var grid = model.Grid.Slots.Select(s => new ContractGridSummary(
            s.Kind, s.Weekday, s.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
            s.Evidence.Observed, s.Evidence.Expected, s.Evidence.Summary)).ToList();
        var pools = ReleaseGrid.AllKinds.Select(k => model.Pools[k]).Select(p => new ContractPoolSummary(
            p.Kind, p.Candidates.Count, p.GapSeconds / 86400d, p.GapSamples, p.LengthSeconds / 86400d,
            p.Evidence.Summary)).ToList();
        var colleggtibles = entry.Colleggtibles
            ?? throw new PredictionsNotReadyException(PredictionDomainKey.Colleggtibles, trainer.Status(PredictionDomainKey.Colleggtibles));
        return new ContractModelResponse(
            model.TrainedAt, grid, pools,
            new TunedStrategySummary(
                entry.Tuned.Strategy.Label, entry.Tuned.Scoreboard, entry.Tuned.Scoreboard[0].Result, entry.Tuned.RunnerUpDelta),
            new TunedStrategySummary(
                colleggtibles.Strategy.Label, colleggtibles.Scoreboard, colleggtibles.Scoreboard[0].Result, colleggtibles.RunnerUpDelta));
    }

    public Task<ContractCacheEntry> TunedAsync(CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        long v = version.Version;
        if (cache.TryGet(v, out var cached)) return Task.FromResult(cached);
        throw new PredictionsNotReadyException(PredictionDomainKey.Contracts, trainer.Status(PredictionDomainKey.Contracts));
    }

    public async Task<IReadOnlyList<ContractReleaseSample>> SamplesAsync(CancellationToken ct = default) =>
        (await TunedAsync(ct)).Samples;

    public async Task<IReadOnlyList<ContractReleaseSample>> LoadSamplesAsync(CancellationToken ct) {
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
        return rows
            .Select(r => new ContractReleaseSample(
                r.ContractId, r.Name, UnixSeconds.FromTime(r.StartTime), r.LengthSeconds, r.Leggacy, r.ProphecyEggs,
                r.UltraOnly, r.Egg, r.CustomEggId))
            .OrderBy(s => s.Start)
            .ToList();
    }

    private void WarnLowConformance(ContractModel model) {
        foreach (var slot in model.Grid.Slots) {
            if (slot.Evidence.Expected == 0 || slot.Evidence.Fill >= MinGridConformance) continue;
            logger.LogWarning(
                "Contract release grid conformance for {Kind} is {Share:P0} over {Count} releases in the last 180 days, below {Minimum:P0}",
                slot.Kind, slot.Evidence.Fill, slot.Evidence.Expected, MinGridConformance);
        }
    }

    private const double MinGridConformance = 0.9;
    private const int SnapHorizon = 12;

    internal static double SnapToSlot(double estimate, ContractSlotKind pool, ReleaseGrid grid) {
        if (!UnixSeconds.IsValid(estimate)) return estimate;
        var from = UnixSeconds.ToTime(estimate).AddSeconds(-1);
        foreach (var (slotTime, slot) in grid.Next(from, SnapHorizon)) {
            if (slot.Kind == pool && slotTime >= estimate) return slotTime;
        }

        return estimate;
    }
}
