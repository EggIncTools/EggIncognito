using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed record IpaStoreCheckResult(bool Ok, IReadOnlyList<string> Versions, string Diagnostics);

public sealed class IpaStoreVersionChecker(
    IpaToolStoreVersions store,
    KnownVersionRecorder knownVersions,
    ILogger<IpaStoreVersionChecker> logger) {
    public const string Source = "ipatool";

    public async Task<IpaStoreCheckResult> CheckAsync(CancellationToken ct) {
        var lookup = await store.LookupAsync(ct);
        if (!lookup.Ok) {
            logger.LogWarning("ipatool store check failed: {Diagnostics}", lookup.Diagnostics);
            return new IpaStoreCheckResult(false, [], lookup.Diagnostics);
        }

        var seen = new List<string>();
        foreach (var version in lookup.Versions) {
            await knownVersions.RecordAsync("ios", version.AppVersion, Source, ct);
            seen.Add(version.AppVersion);
        }

        logger.LogInformation("ipatool store check recorded {Count} version(s): {Versions}",
            seen.Count, string.Join(", ", seen));
        return new IpaStoreCheckResult(true, seen, lookup.Diagnostics);
    }
}
