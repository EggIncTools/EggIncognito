using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Runner.Data;

namespace EggIncognito.Runner.Harvest;

public sealed record HarvestApiResult(int Status, string? DeviceId, object? Body, string? Error);

public sealed class HarvestApi(string secret, RunnerDb db, HarvestScheduler scheduler) {
    public async Task<HarvestApiResult> PokeAsync(string? authorizationHeader, string id, bool force) {
        if (!BearerAuth.Matches(authorizationHeader, secret)) return new HarvestApiResult(401, id, null, "unauthorized");
        using var ctx = db.NewContext();
        var device = await new DeviceStatusStore(ctx).GetAsync(id, CancellationToken.None);
        if (device is null) return new HarvestApiResult(404, id, null, "unknown device");
        scheduler.Poke(id, force);
        return new HarvestApiResult(202, id, new { device = id, queued = true, busy = scheduler.Busy(id) }, null);
    }

    public async Task<HarvestApiResult> PokeAllAsync(string? authorizationHeader, bool force) {
        if (!BearerAuth.Matches(authorizationHeader, secret))
            return new HarvestApiResult(401, null, null, "unauthorized");
        await scheduler.PokeAllAsync(CancellationToken.None, force);
        return new HarvestApiResult(202, null, new { queued = true, force }, null);
    }

    public async Task<HarvestApiResult> StateAsync(string? authorizationHeader, string id) {
        if (!BearerAuth.Matches(authorizationHeader, secret)) return new HarvestApiResult(401, id, null, "unauthorized");
        using var ctx = db.NewContext();
        var states = new DeviceStateStore(ctx, TimeProvider.System);
        var row = await states.GetAsync(id, CancellationToken.None);
        if (row is null) return new HarvestApiResult(404, id, null, "no harvest state for device");
        var jobs = new DeviceJobStore(ctx, TimeProvider.System);
        var harvestJob = await jobs.LatestAsync(id, DeviceJobKinds.Harvest, CancellationToken.None);
        IReadOnlyList<DeviceJobLineRow> log = harvestJob is null
            ? []
            : await jobs.LinesAsync(harvestJob.Id, CancellationToken.None);
        return new HarvestApiResult(200, id, new {
            device = row.DeviceId,
            platform = row.Platform,
            appVersion = row.AppVersion,
            build = row.Build,
            clientVersion = row.ClientVersion,
            revision = row.Revision,
            harvestedRevision = row.HarvestedRevision,
            dirty = row.Dirty,
            harvesting = row.Harvesting,
            lastHarvestAt = row.LastHarvestAt,
            lastHarvestStatus = row.LastHarvestStatus,
            lastHarvestNote = row.LastHarvestNote,
            entries = log.Select(l => new {
                ranAt = l.At,
                entry = l.Entry,
                kind = (string?)null,
                outcome = l.Level == DeviceJobLevels.Error ? "failed" : "ok",
                note = l.Text,
                bytes = l.Bytes ?? 0,
                sha256 = l.Sha256
            })
        }, null);
    }
}
