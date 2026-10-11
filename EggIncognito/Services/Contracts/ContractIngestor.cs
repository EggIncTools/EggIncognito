using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Contracts;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Feed.Kinds;
using EggIncognito.Services.Predictions;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Contracts;

public sealed class ContractIngestor(
    EggIncognitoDbContext db,
    ContractDataVersion version,
    TimeProvider time,
    FeedPublisher? publisher = null) {
    private const long IngestLockKey = 305830012;
    public static readonly TimeSpan Window = TimeSpan.FromHours(48);

    public async Task<ContractIngestResult> IngestAsync(
        IReadOnlyList<ContractObservation> observations, CancellationToken ct = default) {
        if (observations.Count == 0) return new ContractIngestResult(0, 0);
        int inserted = 0, updated = 0;
        var added = new List<ContractRelease>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({IngestLockKey})", ct);
        foreach (var obs in observations) {
            ct.ThrowIfCancellationRequested();
            var local = db.ContractReleases.Local.FirstOrDefault(r => SameRelease(r, obs));
            var lo = obs.Start - Window;
            var hi = obs.Start + Window;
            var match = local ?? await db.ContractReleases
                .Where(r => r.ContractId == obs.ContractId && r.StartTime >= lo && r.StartTime <= hi)
                .OrderBy(r => r.StartTime)
                .FirstOrDefaultAsync(ct);
            if (match is null) {
                var row = Create(obs);
                db.ContractReleases.Add(row);
                added.Add(row);
                inserted++;
            } else if (Apply(match, obs)) {
                updated++;
            }
        }
        if (inserted > 0 || updated > 0) await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (inserted > 0 || updated > 0) version.Bump();
        if (publisher is not null) {
            var seenAt = time.GetUtcNow();
            foreach (var row in added) publisher.Publish(ToEvent(row, seenAt, publisher.PageUrl("contracts")));
        }
        return new ContractIngestResult(inserted, updated);
    }

    public static ContractReleasedEvent ToEvent(ContractRelease row, DateTimeOffset seenAt, string pageUrl) =>
        new(row.ContractId, row.Name, row.Egg, row.CustomEggId, row.StartTime, row.EndTime, row.LengthSeconds,
            row.Leggacy, row.UltraOnly, row.ProphecyEggs, row.MaxCoopSize, row.Source, seenAt, pageUrl);

    public static bool SameRelease(ContractRelease row, ContractObservation obs) =>
        row.ContractId == obs.ContractId && (row.StartTime - obs.Start).Duration() <= Window;

    public static ContractRelease Create(ContractObservation obs) => new() {
        ContractId = obs.ContractId,
        Name = obs.Name,
        Egg = obs.Egg,
        CustomEggId = obs.CustomEggId,
        SeasonId = obs.SeasonId,
        StartTime = obs.Start,
        EndTime = obs.End,
        LengthSeconds = obs.LengthSeconds,
        Leggacy = obs.Leggacy,
        UltraOnly = obs.UltraOnly,
        ProphecyEggs = obs.ProphecyEggs,
        CoopAllowed = obs.CoopAllowed,
        MaxCoopSize = obs.MaxCoopSize,
        MinutesPerToken = obs.MinutesPerToken,
        Proto = obs.Proto,
        Source = obs.Source,
        FirstSeenAt = obs.SeenAt,
        LastSeenAt = obs.SeenAt
    };

    public static bool Apply(ContractRelease row, ContractObservation obs) {
        var seenAt = obs.SeenAt;
        if (obs.Source == ContractSources.Carpet && row.Source == ContractSources.Device) {
            if (row.LastSeenAt is not null && seenAt <= row.LastSeenAt) return false;
            row.LastSeenAt = seenAt;
            return true;
        }
        bool changed = false;
        if (row.Name != obs.Name) { row.Name = obs.Name; changed = true; }
        if (row.Egg != obs.Egg) { row.Egg = obs.Egg; changed = true; }
        if (row.CustomEggId != obs.CustomEggId) { row.CustomEggId = obs.CustomEggId; changed = true; }
        if (row.SeasonId != obs.SeasonId) { row.SeasonId = obs.SeasonId; changed = true; }
        if (row.StartTime != obs.Start) { row.StartTime = obs.Start; changed = true; }
        if (row.EndTime != obs.End) { row.EndTime = obs.End; changed = true; }
        if (row.LengthSeconds != obs.LengthSeconds) { row.LengthSeconds = obs.LengthSeconds; changed = true; }
        if (row.Leggacy != obs.Leggacy) { row.Leggacy = obs.Leggacy; changed = true; }
        if (row.UltraOnly != obs.UltraOnly) { row.UltraOnly = obs.UltraOnly; changed = true; }
        if (row.ProphecyEggs != obs.ProphecyEggs) { row.ProphecyEggs = obs.ProphecyEggs; changed = true; }
        if (row.CoopAllowed != obs.CoopAllowed) { row.CoopAllowed = obs.CoopAllowed; changed = true; }
        if (row.MaxCoopSize != obs.MaxCoopSize) { row.MaxCoopSize = obs.MaxCoopSize; changed = true; }
        if (row.MinutesPerToken != obs.MinutesPerToken) { row.MinutesPerToken = obs.MinutesPerToken; changed = true; }
        if (!row.Proto.SequenceEqual(obs.Proto)) { row.Proto = obs.Proto; changed = true; }
        if (row.Source != obs.Source) { row.Source = obs.Source; changed = true; }
        if (row.LastSeenAt is null || seenAt > row.LastSeenAt) { row.LastSeenAt = seenAt; changed = true; }
        return changed;
    }
}
