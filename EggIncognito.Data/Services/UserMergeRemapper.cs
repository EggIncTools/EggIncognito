using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed record UserIdColumn(string Table, string Column, IReadOnlyList<string> Collisions) {
    public string Name => $"{Table}.{Column}";
    public string UpdateSql => $"UPDATE {Table} SET {Column} = {{1}} WHERE {Column} = {{0}}";
}

public interface IUserMergeRemapper {
    Task<DateTimeOffset?> WatermarkAsync(CancellationToken ct);
    Task<int> ApplyAsync(Guid mergedUserId, Guid keptUserId, DateTimeOffset mergedAt, CancellationToken ct);
}

public sealed class UserMergeRemapper(EggIncognitoDbContext db) : IUserMergeRemapper {
    public const string WatermarkKey = "identity.merge_watermark";

    private const string UpsertWatermarkSql =
        "INSERT INTO app_settings (key, value, updated_at, updated_by) VALUES ({0}, {1}, now(), 'merge-sync') "
        + "ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = now(), updated_by = EXCLUDED.updated_by";

    // Collision statements run before the column's UPDATE with {0} = merged id, {1} = kept id; the kept row always wins.
    public static readonly IReadOnlyList<UserIdColumn> Columns = [
        new("api_keys", "owner_user_id", []),
        new("capture_proxy_addrs", "user_id", [
            "DELETE FROM capture_proxy_addrs WHERE user_id = {0} "
            + "AND EXISTS (SELECT 1 FROM capture_proxy_addrs WHERE user_id = {1})",
        ]),
        new("capture_user_cas", "user_id", [
            "DELETE FROM capture_user_cas WHERE user_id = {0} "
            + "AND EXISTS (SELECT 1 FROM capture_user_cas WHERE user_id = {1})",
        ]),
        new("contributed_captures", "contributor_user_id", [
            "DELETE FROM contributed_captures AS m WHERE m.contributor_user_id = {0} AND EXISTS "
            + "(SELECT 1 FROM contributed_captures AS k WHERE k.contributor_user_id = {1} AND k.dedupe_hash = m.dedupe_hash)",
        ]),
        new("doc_images", "owner_user_id", []),
        new("docs", "owner_user_id", []),
        new("env_design_versions", "author_user_id", []),
        new("env_designs", "owner_user_id", []),
        new("feed_subscriptions", "owner_user_id", []),
        new("route_overrides", "updated_by", []),
        new("site_theme_policy", "updated_by_user_id", []),
        new("stored_endpoints", "owner_user_id", []),
        new("stored_routes", "owner_user_id", []),
        new("user_themes", "owner_user_id", [
            "DELETE FROM user_themes AS m WHERE m.owner_user_id = {0} AND EXISTS "
            + "(SELECT 1 FROM user_themes AS k WHERE k.owner_user_id = {1} AND k.slug = m.slug)",
            "UPDATE user_themes AS m SET is_active = false WHERE m.owner_user_id = {0} AND m.is_active AND EXISTS "
            + "(SELECT 1 FROM user_themes AS k WHERE k.owner_user_id = {1} AND k.is_active)",
        ]),
    ];

    public async Task<DateTimeOffset?> WatermarkAsync(CancellationToken ct) {
        string? raw = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == WatermarkKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;
    }

    public async Task<int> ApplyAsync(Guid mergedUserId, Guid keptUserId, DateTimeOffset mergedAt, CancellationToken ct) {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        int moved = 0;
        if (mergedUserId != keptUserId) {
            object[] ids = [mergedUserId, keptUserId];
            foreach (var column in Columns) {
                foreach (string sql in column.Collisions) await db.Database.ExecuteSqlRawAsync(sql, ids, ct);
                moved += await db.Database.ExecuteSqlRawAsync(column.UpdateSql, ids, ct);
            }
        }

        object[] mark = [WatermarkKey, mergedAt.ToString("O", CultureInfo.InvariantCulture)];
        await db.Database.ExecuteSqlRawAsync(UpsertWatermarkSql, mark, ct);
        await tx.CommitAsync(ct);
        return moved;
    }
}
