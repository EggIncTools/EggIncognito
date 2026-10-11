using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Events;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Feed.Kinds;
using EggIncognito.Services.Predictions;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Events;

public sealed class GameEventIngestor(
    EggIncognitoDbContext db,
    EventDataVersion version,
    TimeProvider time,
    FeedPublisher? publisher = null) {
    private const long IngestLockKey = 872634001;

    public async Task<GameEventIngestResult> IngestAsync(
        IReadOnlyList<GameEventObservation> observations, CancellationToken ct = default) {
        if (observations.Count == 0) return new GameEventIngestResult(0, 0);
        int inserted = 0, updated = 0;
        var added = new List<GameEvent>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({IngestLockKey})", ct);
        foreach (var obs in observations) {
            ct.ThrowIfCancellationRequested();
            var local = db.GameEvents.Local.FirstOrDefault(e => GameEventMerge.SameOccurrence(e, obs));
            var lo = obs.Start - GameEventMerge.Window;
            var hi = obs.Start + GameEventMerge.Window;
            var match = local ?? await db.GameEvents
                .Where(e => e.EventId == obs.EventId && e.StartTime >= lo && e.StartTime <= hi)
                .OrderBy(e => e.StartTime)
                .FirstOrDefaultAsync(ct);
            if (match is null) {
                var row = GameEventMerge.Create(obs);
                db.GameEvents.Add(row);
                added.Add(row);
                inserted++;
            } else if (GameEventMerge.Apply(match, obs)) {
                updated++;
            }
        }
        if (inserted > 0 || updated > 0) await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (inserted > 0 || updated > 0) version.Bump();
        if (publisher is not null) {
            var seenAt = time.GetUtcNow();
            foreach (var row in added) publisher.Publish(ToEvent(row, seenAt, publisher.PageUrl("events")));
        }
        return new GameEventIngestResult(inserted, updated);
    }

    public static GameEventAddedEvent ToEvent(GameEvent row, DateTimeOffset seenAt, string pageUrl) =>
        new(row.EventId, row.EventType, row.Message, row.Multiplier, row.Ultra, row.StartTime, row.EndTime,
            row.Source, seenAt, pageUrl);
}
