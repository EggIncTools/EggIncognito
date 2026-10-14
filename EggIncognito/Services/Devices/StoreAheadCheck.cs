using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices;

public static class StoreAheadCheck {
    public static Task<string?> StoreLatestAsync(
        EggIncognitoDbContext db, string platform, CancellationToken ct, bool crossPlatformHint = false) =>
        LatestVersions.StoreAsync(db, platform, ct, crossPlatformHint);

    public static bool IsAhead(string? storeLatest, string? installed) =>
        !string.IsNullOrEmpty(storeLatest) && !string.IsNullOrEmpty(installed)
                                           && VersionCompare.Dotted(storeLatest, installed) > 0;
}
