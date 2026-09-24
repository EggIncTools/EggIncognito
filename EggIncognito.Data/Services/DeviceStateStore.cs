using EggIncognito.Core;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public static class HarvestStatus {
    public const string Never = "never";
    public const string Running = "running";
    public const string Ok = "ok";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Unreachable = "unreachable";
}

public sealed record DeviceRevision(string Platform, string Package, string? AppVersion, string? Build,
    int? ClientVersion) {
    public string Compute(string deviceId) => Hashes.Sha256Hex(
        string.Join('|', deviceId, Platform, AppVersion ?? "", Build ?? "", ClientVersion?.ToString() ?? "", Package));
}

public sealed class DeviceStateStore(EggIncognitoDbContext db, TimeProvider time) {
    public Task<DeviceState?> GetAsync(string deviceId, CancellationToken ct) =>
        db.DeviceStates.AsNoTracking().FirstOrDefaultAsync(s => s.DeviceId == deviceId, ct);

    public async Task<IReadOnlyList<DeviceState>> ListAsync(CancellationToken ct) =>
        await db.DeviceStates.AsNoTracking().OrderBy(s => s.DeviceId).ToListAsync(ct);

    public async Task<DeviceState> ObserveAsync(string deviceId, DeviceRevision observed, CancellationToken ct) {
        var row = await TrackedAsync(deviceId, ct);
        string revision = observed.Compute(deviceId);
        row.Platform = observed.Platform;
        row.Package = observed.Package;
        row.AppVersion = observed.AppVersion;
        row.Build = observed.Build;
        row.ClientVersion = observed.ClientVersion ?? row.ClientVersion;
        row.Revision = revision;
        row.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task RecordClientVersionAsync(string deviceId, int clientVersion, CancellationToken ct) {
        var row = await TrackedAsync(deviceId, ct);
        if (row.ClientVersion == clientVersion) return;
        row.ClientVersion = clientVersion;
        row.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task PokeAsync(string deviceId, CancellationToken ct) {
        var row = await TrackedAsync(deviceId, ct);
        row.Dirty = true;
        row.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> TryBeginAsync(string deviceId, CancellationToken ct) {
        var row = await TrackedAsync(deviceId, ct);
        if (row.Harvesting) {
            row.Dirty = true;
            await db.SaveChangesAsync(ct);
            return false;
        }

        row.Harvesting = true;
        row.Dirty = false;
        row.LastHarvestStatus = HarvestStatus.Running;
        row.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ClearDirtyAsync(string deviceId, CancellationToken ct) {
        var row = await TrackedAsync(deviceId, ct);
        bool was = row.Dirty;
        row.Dirty = false;
        await db.SaveChangesAsync(ct);
        return was;
    }

    public async Task FinishAsync(string deviceId, string status, string? note, string revision,
        CancellationToken ct) {
        var row = await TrackedAsync(deviceId, ct);
        row.Harvesting = false;
        row.LastHarvestStatus = status;
        row.LastHarvestNote = note;
        row.LastHarvestAt = time.GetUtcNow();
        row.UpdatedAt = time.GetUtcNow();
        if (status is HarvestStatus.Ok) row.HarvestedRevision = revision;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> ResetRunningAsync(CancellationToken ct) {
        var stuck = await db.DeviceStates.Where(s => s.Harvesting).ToListAsync(ct);
        if (stuck.Count == 0) return 0;
        foreach (var row in stuck) {
            row.Harvesting = false;
            row.LastHarvestStatus = HarvestStatus.Failed;
            row.LastHarvestNote = "interrupted by agent restart";
        }

        await db.SaveChangesAsync(ct);
        return stuck.Count;
    }

    private async Task<DeviceState> TrackedAsync(string deviceId, CancellationToken ct) {
        var row = await db.DeviceStates.FirstOrDefaultAsync(s => s.DeviceId == deviceId, ct);
        if (row is not null) return row;
        row = new DeviceState { DeviceId = deviceId, UpdatedAt = time.GetUtcNow() };
        db.DeviceStates.Add(row);
        return row;
    }
}
