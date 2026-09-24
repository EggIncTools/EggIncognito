using System.Text.Json;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed record JobRef(long Id, string DeviceId, string Kind);

public sealed record DeviceJobRow(
    long Id, string DeviceId, string Kind, string State, string Trigger,
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt,
    string? Outcome, string? Message,
    bool? Reachable, string? AppVersion, string? Build, int? ClientVersion,
    string? Revision, string? Detail);

public sealed record DeviceJobLineRow(
    long Id, DateTimeOffset At, string Level, string Text,
    string? Entry, long? Bytes, string? Sha256);

public sealed record DeviceJobFacts(
    bool? Reachable = null, string? AppVersion = null, string? Build = null,
    int? ClientVersion = null, string? Revision = null, object? Detail = null);

public sealed record DeviceProbeStats(
    string DeviceId,
    int Total,
    int ReachableCount,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    int ConsecutiveFailures,
    IReadOnlyDictionary<string, int> ResultCounts);

public interface IDeviceJobSink {
    void Touched(string deviceId);
}

public sealed class DeviceJobStore(EggIncognitoDbContext db, TimeProvider time, IDeviceJobSink? sink = null) {
    public static readonly TimeSpan AbandonAfter = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ProbeRetention = TimeSpan.FromDays(14);
    private static readonly TimeSpan OtherRetention = TimeSpan.FromDays(90);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);
    private static DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    private static readonly string[] FailureOutcomes =
        ["failed", "partial", "error", "unreachable", "unsupported", "abandoned"];

    public static bool IsAbandoned(DateTimeOffset startedAt, DateTimeOffset now) =>
        now - startedAt >= AbandonAfter;

    public static string StateFor(string? outcome) =>
        outcome is not null && FailureOutcomes.Contains(outcome, StringComparer.OrdinalIgnoreCase)
            ? DeviceJobStates.Failed
            : DeviceJobStates.Succeeded;

    public static DeviceProbeStats StatsFor(string deviceId,
        IReadOnlyList<(DateTimeOffset StartedAt, bool? Reachable, string? Outcome)> newestFirst,
        DateTimeOffset cutoff) {
        var windowed = newestFirst.Where(r => r.StartedAt >= cutoff).ToList();
        int consecutive = 0;
        foreach (var (_, reachable, _) in newestFirst) {
            if (reachable == true) break;
            consecutive++;
        }

        return new DeviceProbeStats(
            deviceId,
            windowed.Count,
            windowed.Count(r => r.Reachable == true),
            newestFirst.Where(r => r.Reachable == true).Select(r => (DateTimeOffset?)r.StartedAt).FirstOrDefault(),
            newestFirst.Where(r => r.Reachable != true).Select(r => (DateTimeOffset?)r.StartedAt).FirstOrDefault(),
            consecutive,
            windowed.Where(r => r.Outcome is not null).GroupBy(r => r.Outcome!)
                .ToDictionary(g => g.Key, g => g.Count()));
    }

    public Task<JobRef?> TryStartAsync(string deviceId, string kind, string trigger, string message,
        CancellationToken ct = default) =>
        TryStartAsync(deviceId, kind, trigger, message, null, ct);

    public async Task<JobRef?> TryStartAsync(string deviceId, string kind, string trigger, string message,
        DeviceJobFacts? facts, CancellationToken ct = default) {
        var now = time.GetUtcNow();
        var running = await db.DeviceJobs
            .Where(j => j.DeviceId == deviceId && j.State == DeviceJobStates.Running)
            .OrderByDescending(j => j.Id)
            .FirstOrDefaultAsync(ct);

        if (running is not null) {
            if (!IsAbandoned(running.StartedAt, now)) return null;
            running.State = DeviceJobStates.Failed;
            running.Outcome = "abandoned";
            running.Message = "abandoned after 30 minutes with no completion";
            running.FinishedAt = now;
        }

        var job = new DeviceJob {
            DeviceId = deviceId,
            Kind = kind,
            State = DeviceJobStates.Running,
            Trigger = trigger,
            StartedAt = now,
            Message = message
        };
        Apply(job, facts);
        db.DeviceJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await TouchedAsync(deviceId, ct);
        return new JobRef(job.Id, deviceId, kind);
    }

    public async Task ProgressAsync(JobRef job, string text, string level = DeviceJobLevels.Info,
        CancellationToken ct = default) {
        var now = time.GetUtcNow();
        db.DeviceJobLines.Add(new DeviceJobLine { JobId = job.Id, At = now, Level = level, Text = text });
        var row = await db.DeviceJobs.FirstOrDefaultAsync(j => j.Id == job.Id, ct);
        row?.Message = text;
        await db.SaveChangesAsync(ct);
        await TouchedAsync(job.DeviceId, ct);
    }

    public async Task LineAsync(JobRef job, string entry, string outcome, string? note, long bytes, string? sha256,
        CancellationToken ct = default) {
        db.DeviceJobLines.Add(new DeviceJobLine {
            JobId = job.Id,
            At = time.GetUtcNow(),
            Level = outcome == "failed" ? DeviceJobLevels.Error : DeviceJobLevels.Info,
            Text = note is null ? outcome : $"{outcome}: {note}",
            Entry = entry,
            Bytes = bytes,
            Sha256 = sha256
        });
        await db.SaveChangesAsync(ct);
        await TouchedAsync(job.DeviceId, ct);
    }

    public async Task FinishAsync(JobRef job, string outcome, string? message, DeviceJobFacts? facts = null,
        CancellationToken ct = default) {
        var row = await db.DeviceJobs.FirstOrDefaultAsync(j => j.Id == job.Id, ct);
        if (row is null) return;
        row.State = StateFor(outcome);
        row.Outcome = outcome;
        row.Message = message ?? row.Message;
        row.FinishedAt = time.GetUtcNow();
        Apply(row, facts);
        await db.SaveChangesAsync(ct);
        await TouchedAsync(job.DeviceId, ct);
        await PruneAsync(ct);
    }

    public async Task FailAsync(JobRef job, string message, CancellationToken ct = default) {
        var row = await db.DeviceJobs.FirstOrDefaultAsync(j => j.Id == job.Id, ct);
        if (row is null) return;
        row.State = DeviceJobStates.Failed;
        row.Outcome = "error";
        row.Message = message;
        row.FinishedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await TouchedAsync(job.DeviceId, ct);
    }

    public async Task<long?> CancelRunningAsync(string deviceId, string kind, string message,
        CancellationToken ct = default) {
        var row = await db.DeviceJobs
            .Where(j => j.DeviceId == deviceId && j.Kind == kind && j.State == DeviceJobStates.Running)
            .OrderByDescending(j => j.Id)
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        row.State = DeviceJobStates.Cancelled;
        row.Outcome = "cancelled";
        row.Message = message;
        row.FinishedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await TouchedAsync(deviceId, ct);
        return row.Id;
    }

    public async Task CancelAsync(JobRef job, string message, CancellationToken ct = default) {
        var row = await db.DeviceJobs.FirstOrDefaultAsync(j => j.Id == job.Id, ct);
        if (row is null) return;
        row.State = DeviceJobStates.Cancelled;
        row.Outcome = "cancelled";
        row.Message = message;
        row.FinishedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await TouchedAsync(job.DeviceId, ct);
    }

    public async Task<long> RecordAsync(string deviceId, string kind, string trigger, string outcome,
        string? message, DeviceJobFacts? facts = null, CancellationToken ct = default) {
        var now = time.GetUtcNow();
        var row = new DeviceJob {
            DeviceId = deviceId,
            Kind = kind,
            State = DeviceJobStates.Succeeded,
            Trigger = trigger,
            StartedAt = now,
            FinishedAt = now,
            Outcome = outcome,
            Message = message
        };
        Apply(row, facts);
        db.DeviceJobs.Add(row);
        await db.SaveChangesAsync(ct);
        await TouchedAsync(deviceId, ct);
        await PruneAsync(ct);
        return row.Id;
    }

    private async Task TouchedAsync(string deviceId, CancellationToken ct) {
        sink?.Touched(deviceId);
        await PgNotify.SendAsync(db, PgChannels.DeviceJobs, deviceId, ct);
    }

    public async Task<IReadOnlyList<DeviceJobRow>> HistoryAsync(string deviceId, int n, string? kind = null,
        CancellationToken ct = default) {
        var q = db.DeviceJobs.AsNoTracking().Where(j => j.DeviceId == deviceId);
        if (!string.IsNullOrEmpty(kind)) q = q.Where(j => j.Kind == kind);
        var rows = await q.OrderByDescending(j => j.Id).Take(Math.Clamp(n, 1, 200)).ToListAsync(ct);
        return [.. rows.Select(Map)];
    }

    public async Task<IReadOnlyList<DeviceJobRow>> PageAsync(string deviceId, int n, long? before,
        string? kind = null, CancellationToken ct = default) {
        var q = db.DeviceJobs.AsNoTracking().Where(j => j.DeviceId == deviceId);
        if (!string.IsNullOrEmpty(kind)) q = q.Where(j => j.Kind == kind);
        if (before is { } cursor) q = q.Where(j => j.Id < cursor);
        var rows = await q.OrderByDescending(j => j.Id).Take(Math.Clamp(n, 1, 500)).ToListAsync(ct);
        return [.. rows.Select(Map)];
    }

    public async Task<IReadOnlyList<DeviceJobLineRow>> LinesAsync(long jobId, CancellationToken ct = default) {
        var rows = await db.DeviceJobLines.AsNoTracking()
            .Where(l => l.JobId == jobId).OrderBy(l => l.Id).ToListAsync(ct);
        return [.. rows.Select(l => new DeviceJobLineRow(l.Id, l.At, l.Level, l.Text, l.Entry, l.Bytes, l.Sha256))];
    }

    public async Task<IReadOnlyList<DeviceJobRow>> RunningAsync(CancellationToken ct = default) {
        var rows = await db.DeviceJobs.AsNoTracking()
            .Where(j => j.State == DeviceJobStates.Running).OrderBy(j => j.DeviceId).ToListAsync(ct);
        return [.. rows.Select(Map)];
    }

    public async Task<DeviceJobRow?> LatestAsync(string deviceId, string kind, CancellationToken ct = default) {
        var row = await db.DeviceJobs.AsNoTracking()
            .Where(j => j.DeviceId == deviceId && j.Kind == kind)
            .OrderByDescending(j => j.Id).FirstOrDefaultAsync(ct);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<DeviceJobRow>> LatestPerDeviceAsync(string kind,
        CancellationToken ct = default) {
        var rows = await db.DeviceJobs.AsNoTracking()
            .Where(j => j.Kind == kind && j.Id == db.DeviceJobs
                .Where(x => x.DeviceId == j.DeviceId && x.Kind == kind).Max(x => x.Id))
            .ToListAsync(ct);
        return [.. rows.Select(Map)];
    }

    public async Task<IReadOnlyList<DeviceProbeStats>> StatsAsync(IReadOnlyList<string> deviceIds, TimeSpan window,
        CancellationToken ct = default) {
        if (deviceIds.Count == 0) return [];
        var cutoff = time.GetUtcNow() - window;
        var rows = await db.DeviceJobs.AsNoTracking()
            .Where(j => j.Kind == DeviceJobKinds.Probe && deviceIds.Contains(j.DeviceId))
            .OrderByDescending(j => j.Id)
            .Select(j => new { j.DeviceId, j.Id, j.StartedAt, j.Reachable, j.Outcome })
            .Take(5000)
            .ToListAsync(ct);

        return [.. deviceIds.Select(id => StatsFor(id,
            [.. rows.Where(r => r.DeviceId == id).Select(r => (r.StartedAt, r.Reachable, r.Outcome))],
            cutoff))];
    }

    public async Task<IReadOnlyList<(string DeviceId, long Watermark, int Unfinished)>> WatermarksAsync(
        CancellationToken ct = default) {
        var rows = await db.DeviceJobs.AsNoTracking()
            .GroupBy(j => j.DeviceId)
            .Select(g => new {
                DeviceId = g.Key,
                Mark = g.Max(x => x.Id),
                Unfinished = g.Count(x => x.FinishedAt == null)
            })
            .ToListAsync(ct);
        return [.. rows.Select(r => (r.DeviceId, r.Mark, r.Unfinished))];
    }

    private async Task PruneAsync(CancellationToken ct) {
        var now = time.GetUtcNow();
        if (now - _lastPrune < PruneInterval) return;
        _lastPrune = now;
        var probeCutoff = now - ProbeRetention;
        var otherCutoff = now - OtherRetention;
        await db.DeviceJobs
            .Where(j => j.Kind == DeviceJobKinds.Probe && j.StartedAt < probeCutoff)
            .ExecuteDeleteAsync(ct);
        await db.DeviceJobs
            .Where(j => j.Kind != DeviceJobKinds.Probe && j.StartedAt < otherCutoff)
            .ExecuteDeleteAsync(ct);
    }

    private static void Apply(DeviceJob row, DeviceJobFacts? facts) {
        if (facts is null) return;
        row.Reachable = facts.Reachable;
        row.AppVersion = facts.AppVersion;
        row.Build = facts.Build;
        row.ClientVersion = facts.ClientVersion;
        row.Revision = facts.Revision;
        row.Detail = facts.Detail is null ? null : JsonSerializer.Serialize(facts.Detail);
    }

    private static DeviceJobRow Map(DeviceJob j) => new(
        j.Id, j.DeviceId, j.Kind, j.State, j.Trigger, j.StartedAt, j.FinishedAt,
        j.Outcome, j.Message, j.Reachable, j.AppVersion, j.Build, j.ClientVersion, j.Revision, j.Detail);
}
