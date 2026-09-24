using EggIncognito.Data.Services;
using EggIncognito.Models.Contracts;
using EggIncognito.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Predictions;

public readonly record struct ContractReleaseSample(
    string ContractId, string Name, double Start, int ProphecyEggs, bool UltraOnly);

public sealed class ContractPredictor(
    EggIncognitoDbContext db, ContractDataVersion version, ContractPredictionCache cache,
    ILogger<ContractPredictor> logger, TimeProvider time) {
    private const int TopCandidates = 5;
    private const int SnapHorizon = 12;
    private const int MinGapSamples = 4;
    private const double RecencyGapMultiplier = 2d;
    private const double FallbackCutoffSeconds = 3 * 365.25d * 86400d;
    private const double ConformanceWindowSeconds = 180d * 86400d;
    private const double GridToleranceSeconds = 300d;
    private const double MinGridConformance = 0.9;

    private static readonly ContractSlotKind[] AllPools = [
        ContractSlotKind.NewContract, ContractSlotKind.Leggacy,
        ContractSlotKind.PeLeggacy, ContractSlotKind.PeLeggacyUltra
    ];

    public async Task<ContractPredictionResponse> GetSlotsAsync(int horizonSlots, CancellationToken ct = default) {
        var data = await GetDataAsync(ct);
        var now = time.GetUtcNow();
        double nowSeconds = UnixSeconds.FromTime(now);
        var slots = ContractSlots.Next(now, horizonSlots)
            .Select(s => new ContractSlotPrediction(s.Time, s.Kind, Top(data, s.Kind, nowSeconds)))
            .ToList();
        return new ContractPredictionResponse(nowSeconds, slots);
    }

    public async Task<ContractNextEstimate?> GetContractAsync(string contractId, CancellationToken ct = default) {
        var data = await GetDataAsync(ct);
        double now = UnixSeconds.FromTime(time.GetUtcNow());
        foreach (var pool in AllPools) {
            var candidate = data.Pools[pool].FirstOrDefault(c => c.ContractId == contractId);
            if (candidate is null) continue;
            double? estimate = data.PoolGapSeconds[pool] is { } gap
                ? SnapToSlot(Math.Max(candidate.LastReleased + gap, now), pool)
                : null;
            return new ContractNextEstimate(
                candidate.ContractId, candidate.Name, candidate.LastReleased,
                estimate, pool, candidate.Releases, data.PoolGapSamples[pool]);
        }
        return null;
    }

    private async Task<ContractPredictionData> GetDataAsync(CancellationToken ct) {
        long v = version.Version;
        if (cache.Version == v && cache.Value is { } cached) return cached;

        var rows = await db.ContractReleases.AsNoTracking()
            .Select(r => new { r.ContractId, r.Name, r.StartTime, r.ProphecyEggs, r.UltraOnly })
            .ToListAsync(ct);
        var samples = rows
            .Select(r => new ContractReleaseSample(
                r.ContractId, r.Name, UnixSeconds.FromTime(r.StartTime), r.ProphecyEggs, r.UltraOnly))
            .ToList();

        var data = BuildData(samples);
        CheckGridConformance(samples, UnixSeconds.FromTime(time.GetUtcNow()));
        lock (cache) {
            cache.Value = data;
            cache.Version = v;
        }
        return data;
    }

    private void CheckGridConformance(IReadOnlyList<ContractReleaseSample> samples, double now) {
        var recent = samples.Where(s => now - s.Start <= ConformanceWindowSeconds).ToList();
        if (recent.Count == 0) return;
        double share = recent.Count(s => ContractSlots.IsGridSlot(s.Start, GridToleranceSeconds))
            / (double)recent.Count;
        if (share >= MinGridConformance) return;
        logger.LogWarning(
            "Contract release grid conformance {Share:P0} over {Count} releases in the last 180 days is below {Minimum:P0}",
            share, recent.Count, MinGridConformance);
    }

    internal static ContractPredictionData BuildData(IReadOnlyList<ContractReleaseSample> samples) {
        var members = new Dictionary<ContractSlotKind, List<ContractCandidate>>();
        var gaps = new Dictionary<ContractSlotKind, List<double>>();
        foreach (var pool in AllPools) {
            members[pool] = [];
            gaps[pool] = [];
        }

        foreach (var group in samples.GroupBy(s => s.ContractId, StringComparer.Ordinal)) {
            var ordered = group.OrderBy(s => s.Start).ToList();
            var pool = PoolFor(ordered.Max(s => s.ProphecyEggs), ordered.Exists(s => s.UltraOnly));
            var newest = ordered[^1];
            members[pool].Add(new ContractCandidate(group.Key, newest.Name, newest.Start, ordered.Count));
            for (int i = 1; i < ordered.Count; i++) gaps[pool].Add(ordered[i].Start - ordered[i - 1].Start);
        }

        var pools = new Dictionary<ContractSlotKind, IReadOnlyList<ContractCandidate>>();
        var poolGaps = new Dictionary<ContractSlotKind, double?>();
        var poolGapSamples = new Dictionary<ContractSlotKind, int>();
        foreach (var pool in AllPools) {
            pools[pool] = members[pool].OrderBy(c => c.LastReleased).ToList();
            poolGaps[pool] = PoolGap(pool, gaps[pool]);
            poolGapSamples[pool] = gaps[pool].Count;
        }
        return new ContractPredictionData(pools, poolGaps, poolGapSamples);
    }

    internal static ContractSlotKind PoolFor(int peMax, bool everUltra) {
        if (peMax <= 0) return ContractSlotKind.Leggacy;
        return everUltra ? ContractSlotKind.PeLeggacyUltra : ContractSlotKind.PeLeggacy;
    }

    internal static double SnapToSlot(double estimate, ContractSlotKind pool) {
        if (!UnixSeconds.IsValid(estimate)) return estimate;
        var from = UnixSeconds.ToTime(estimate).AddSeconds(-1);
        foreach (var (time, kind) in ContractSlots.Next(from, SnapHorizon)) {
            if (kind == pool && time >= estimate) return time;
        }
        return estimate;
    }

    private static double? PoolGap(ContractSlotKind pool, List<double> gaps) {
        if (pool == ContractSlotKind.NewContract) return null;
        return gaps.Count >= MinGapSamples ? RobustStats.Median(gaps) : null;
    }

    internal static IReadOnlyList<ContractCandidate> Top(
        ContractPredictionData data, ContractSlotKind pool, double now) {
        double cutoff = data.PoolGapSeconds[pool] is { } gap
            ? RecencyGapMultiplier * gap
            : FallbackCutoffSeconds;
        return data.Pools[pool].Where(c => now - c.LastReleased <= cutoff).Take(TopCandidates).ToList();
    }
}
