using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Devices;

public sealed class KnownVersionRecorder(
    IServiceScopeFactory scopeFactory,
    ILogger<KnownVersionRecorder> logger,
    TimeProvider time) {
    public async Task RecordAsync(string platform, string appVersion, string source, CancellationToken ct) {
        try {
            using var scope = scopeFactory.CreateScope();
            if (scope.ServiceProvider.GetService(typeof(EggIncognitoDbContext)) is not EggIncognitoDbContext db)
                return;
            bool exists = await db.KnownVersions.AsNoTracking()
                .AnyAsync(k => k.Platform == platform && k.AppVersion == appVersion, ct);
            if (exists) return;
            db.KnownVersions.Add(new KnownVersion {
                Platform = platform,
                AppVersion = appVersion,
                Source = source,
                FirstSeen = time.GetUtcNow()
            });
            await db.SaveChangesAsync(ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "known-version record failed for {Platform} {Version} ({Source})",
                platform, appVersion, source);
        }
    }
}
