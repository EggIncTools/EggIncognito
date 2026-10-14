using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Data.Services;

public static class DeviceProbeRunner {
    public static string Classify(Device d, DeviceProbeResult r, string? extractedLatestBuild,
        string? extractedLatestAppVersion)
        => Classify(r, d.Platform, extractedLatestBuild, extractedLatestAppVersion);

    public static string Classify(DeviceProbeResult r, string platform, string? extractedLatestBuild,
        string? extractedLatestAppVersion) {
        if (!r.Reachable) return "unreachable";
        if (string.IsNullOrEmpty(r.InstalledAppVersion)) return "error";

        if (Platforms.Matches(platform, Platforms.Ios)) {
            return string.IsNullOrEmpty(extractedLatestAppVersion)
                ? "new_version"
                : VersionCompare.Dotted(r.InstalledAppVersion, extractedLatestAppVersion) > 0
                    ? "new_version"
                    : "no_change";
        }

        return extractedLatestBuild is null
            ? "new_version"
            : long.TryParse(r.InstalledBuild, out long inst) && long.TryParse(extractedLatestBuild, out long ext)
                ? inst > ext ? "new_version" : "no_change"
                : VersionCompare.Dotted(r.InstalledAppVersion, extractedLatestAppVersion) > 0
                    ? "new_version"
                    : "no_change";
    }

    public static async Task<DeviceJobRow> ProbeOneAsync(
        Device d, string triggeredBy, IDevicePlatforms platforms, DeviceJobStore jobs,
        EggIncognitoDbContext db, ILogger logger, TimeProvider time, CancellationToken ct) {
        var result = await platforms.For(d.Platform)
            .ProbeAsync(new DeviceTarget(d.Id, d.Platform, d.Target, d.Package), ct);

        var extracted = await LatestVersions.ExtractedAsync(db, d.Platform, ct);
        string? latestAvailable = await LatestVersions.StoreAsync(db, d.Platform, ct);

        string resultCode = Classify(d, result, extracted.Build, extracted.AppVersion);

        long id = await jobs.RecordAsync(
            d.Id, DeviceJobKinds.Probe, triggeredBy, resultCode, result.Note,
            new DeviceJobFacts(
                Reachable: result.Reachable,
                AppVersion: result.InstalledAppVersion,
                Build: result.InstalledBuild,
                Detail: latestAvailable is null ? null : new { latestAvailable }),
            ct);

        var now = time.GetUtcNow();
        var row = new DeviceJobRow(
            id, d.Id, DeviceJobKinds.Probe, DeviceJobStates.Succeeded, triggeredBy,
            now, now, resultCode, result.Note,
            result.Reachable, result.InstalledAppVersion, result.InstalledBuild, null, null, null);

        if (result.Reachable) {
            logger.LogInformation("device probe: {Id} reachable installed={App} build={Build} -> {Result}",
                d.Id, result.InstalledAppVersion ?? "?", result.InstalledBuild ?? "-", resultCode);
        } else {
            logger.LogInformation("device probe: {Id} unreachable ({Note})", d.Id, result.Note);
        }

        return row;
    }
}
