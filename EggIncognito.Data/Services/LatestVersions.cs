using EggIncognito.Core.Services.Devices;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed record ExtractedLatest(string? AppVersion, string? Build);

public static class LatestVersions {
    public static async Task<ExtractedLatest> ExtractedAsync(EggIncognitoDbContext db, string platform, CancellationToken ct) {
        var rows = await db.ProtoVersions.AsNoTracking()
            .Where(v => EF.Functions.ILike(v.Platform, platform) && (v.DeletedAt == null || v.CanonicalId != null))
            .Select(v => new { v.Build, v.AppVersion })
            .ToListAsync(ct);
        string? appVersion = MaxVersion(rows.Select(r => r.AppVersion));
        string? build = Platforms.Matches(platform, Platforms.Android)
            ? rows.Select(r => r.Build).Where(b => long.TryParse(b, out _)).OrderByDescending(long.Parse).FirstOrDefault()
            : null;
        return new ExtractedLatest(appVersion, build);
    }

    public static async Task<string?> StoreAsync(EggIncognitoDbContext db, string platform, CancellationToken ct, bool crossPlatform = false) {
        var query = db.KnownVersions.AsNoTracking();
        if (!crossPlatform) query = query.Where(k => EF.Functions.ILike(k.Platform, platform));
        var versions = await query.Select(k => k.AppVersion).ToListAsync(ct);
        return MaxVersion(versions);
    }

    private static string? MaxVersion(IEnumerable<string?> versions)
        => versions.Where(v => !string.IsNullOrEmpty(v)).OrderByDescending(v => v, VersionCompare.DottedComparer).FirstOrDefault();
}
