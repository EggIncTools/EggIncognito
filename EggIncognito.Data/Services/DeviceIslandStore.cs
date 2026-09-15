using System.Text.RegularExpressions;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed partial class DeviceIslandStore(EggIncognitoDbContext db, TimeProvider time) {
    [GeneratedRegex(@"UserInfo\{(\d+):([^:}]*):")]
    private static partial Regex UserInfoRegex();

    public async Task<IReadOnlyList<DeviceIsland>> ListAsync(string deviceId, CancellationToken ct = default) {
        var rows = await db.DeviceIslands.AsNoTracking()
            .Where(x => x.DeviceId == deviceId)
            .OrderBy(x => x.AndroidUserId)
            .ToListAsync(ct);
        return [.. rows.Select(Project)];
    }

    public async Task<DeviceIsland?> GetAsync(string deviceId, int androidUserId, CancellationToken ct = default) {
        var row = await db.DeviceIslands.AsNoTracking()
            .FirstOrDefaultAsync(x => x.DeviceId == deviceId && x.AndroidUserId == androidUserId, ct);
        return row is null ? null : Project(row);
    }

    public async Task<DeviceIsland> UpsertAsync(
        string deviceId, int androidUserId, string label, bool provisioned, string? eggAccountId,
        CancellationToken ct = default) {
        var row = await db.DeviceIslands
            .FirstOrDefaultAsync(x => x.DeviceId == deviceId && x.AndroidUserId == androidUserId, ct);
        if (row is null) {
            row = new DeviceIslandRow {
                DeviceId = deviceId,
                AndroidUserId = androidUserId,
                Label = label,
                Provisioned = provisioned,
                EggAccountId = eggAccountId,
                CreatedAt = time.GetUtcNow()
            };
            db.DeviceIslands.Add(row);
        } else {
            row.Label = label;
            row.Provisioned = provisioned;
            if (eggAccountId is not null) row.EggAccountId = eggAccountId;
        }

        await db.SaveChangesAsync(ct);
        return Project(row);
    }

    public async Task SetProvisionedAsync(
        string deviceId, int androidUserId, bool provisioned, CancellationToken ct = default) {
        var row = await db.DeviceIslands
            .FirstOrDefaultAsync(x => x.DeviceId == deviceId && x.AndroidUserId == androidUserId, ct);
        if (row is null) return;
        row.Provisioned = provisioned;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetEggAccountAsync(
        string deviceId, int androidUserId, string? eggAccountId, CancellationToken ct = default) {
        var row = await db.DeviceIslands
            .FirstOrDefaultAsync(x => x.DeviceId == deviceId && x.AndroidUserId == androidUserId, ct);
        if (row is null) return;
        row.EggAccountId = eggAccountId;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(string deviceId, int androidUserId, CancellationToken ct = default) {
        var row = await db.DeviceIslands
            .FirstOrDefaultAsync(x => x.DeviceId == deviceId && x.AndroidUserId == androidUserId, ct);
        if (row is null) return;
        db.DeviceIslands.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DeviceIsland>> ReconcileAsync(
        string deviceId, string pmListUsersOutput, IReadOnlySet<int>? provisionedOnDevice = null,
        CancellationToken ct = default) {
        var onDevice = ParseUsers(pmListUsersOutput);
        var rows = await db.DeviceIslands.Where(x => x.DeviceId == deviceId).ToListAsync(ct);
        var byUser = rows.ToDictionary(x => x.AndroidUserId);

        foreach ((int androidUserId, string label) in onDevice) {
            bool? provisioned = provisionedOnDevice?.Contains(androidUserId);
            if (byUser.TryGetValue(androidUserId, out var existing)) {
                if (label.Length > 0 && !string.Equals(existing.Label, label, StringComparison.Ordinal))
                    existing.Label = label;
                if (provisioned is { } known && existing.Provisioned != known) existing.Provisioned = known;
                continue;
            }

            db.DeviceIslands.Add(new DeviceIslandRow {
                DeviceId = deviceId,
                AndroidUserId = androidUserId,
                Label = label.Length > 0 ? label : $"user {androidUserId}",
                Provisioned = provisioned ?? false,
                EggAccountId = null,
                CreatedAt = time.GetUtcNow()
            });
        }

        var present = onDevice.Select(u => u.AndroidUserId).ToHashSet();
        foreach (var row in rows.Where(r => !present.Contains(r.AndroidUserId))) db.DeviceIslands.Remove(row);

        await db.SaveChangesAsync(ct);
        return await ListAsync(deviceId, ct);
    }

    public static IReadOnlyList<(int AndroidUserId, string Label)> ParseUsers(string pmListUsersOutput) {
        var users = new List<(int, string)>();
        foreach (Match match in UserInfoRegex().Matches(pmListUsersOutput)) {
            if (!int.TryParse(match.Groups[1].Value, out int id) || id == 0) continue;
            users.Add((id, match.Groups[2].Value.Trim()));
        }

        return users;
    }

    private static DeviceIsland Project(DeviceIslandRow row) =>
        new(row.DeviceId, row.AndroidUserId, row.Label, row.Provisioned, row.EggAccountId, row.CreatedAt);
}
