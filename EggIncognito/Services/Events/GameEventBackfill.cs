using System.Text.Json;
using EggIncognito.Core.Services;
using EggIncognito.Data.Services;
using EggIncognito.Models.Events;
using Ei;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Events;

public sealed class GameEventBackfill(
    EggIncognitoDbContext db,
    GameEventIngestor ingestor,
    IHttpClientFactory httpFactory,
    ILogger<GameEventBackfill> logger) {
    public const string DefaultCarpetUrl =
        "https://raw.githubusercontent.com/carpetsage/egg/master/periodicals/data/events.json";

    private static readonly JsonSerializerOptions CarpetJson = JsonPresets.Web;

    public async Task<EventBackfillResult> SweepSnapshotsAsync(CancellationToken ct = default) {
        int inserted = 0, updated = 0, scanned = 0, skipped = 0;
        var ids = await db.PeriodicalsSnapshots
            .OrderBy(s => s.CapturedAt)
            .Select(s => s.Id)
            .ToListAsync(ct);
        foreach (long id in ids) {
            var snap = await db.PeriodicalsSnapshots.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, ct);
            if (snap is null) continue;
            scanned++;
            try {
                var response = (PeriodicalsResponse)JsonParser.Default
                    .Parse(snap.ResponseJson, PeriodicalsResponse.Descriptor);
                var observations = GameEventMapper.FromPeriodicals(response, snap.CapturedAt);
                var result = await ingestor.IngestAsync(observations, ct);
                inserted += result.Inserted;
                updated += result.Updated;
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                skipped++;
                logger.LogWarning(ex, "snapshot {Id} failed during event sweep", id);
            } finally {
                db.ChangeTracker.Clear();
            }
        }
        return new EventBackfillResult(scanned, inserted, updated, skipped);
    }

    public async Task<EventBackfillResult> ImportCarpetAsync(string? url, CancellationToken ct = default) {
        string target = string.IsNullOrWhiteSpace(url) ? DefaultCarpetUrl : url.Trim();
        using var client = httpFactory.CreateClient("carpet");
        string json = await client.GetStringAsync(target, ct);
        var rows = JsonSerializer.Deserialize<List<CarpetEvent>>(json, CarpetJson) ?? [];
        var observations = GameEventMapper.FromCarpet(rows);
        var result = await ingestor.IngestAsync(observations, ct);
        int skipped = rows.Count - observations.Count;
        logger.LogInformation(
            "carpet import from {Url}: {Rows} rows, {Inserted} inserted, {Updated} updated, {Skipped} skipped",
            target, rows.Count, result.Inserted, result.Updated, skipped);
        return new EventBackfillResult(rows.Count, result.Inserted, result.Updated, skipped);
    }
}
