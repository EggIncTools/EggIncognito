using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed record DeviceStatusInputs(
    bool IsAdmin,
    IReadOnlyDictionary<string, DeviceJobRow> Probes,
    IReadOnlyDictionary<string, DeviceJobRow> Updates,
    IReadOnlyDictionary<string, string?> StoreLatest,
    DeviceVersionIndex Versions,
    IReadOnlyDictionary<string, int> CapturedClientVersions,
    GameBinaryProvider? Binaries,
    Func<string, int> CapturePortFor);

public static class DeviceStatusProjector {
    public static DeviceStatusRow Project(Device device, DeviceStatusInputs inputs) {
        var probe = inputs.Probes.GetValueOrDefault(device.Id);
        var update = inputs.Updates.GetValueOrDefault(device.Id);
        string? storeLatest = inputs.StoreLatest.GetValueOrDefault(device.Platform);
        bool isAdmin = inputs.IsAdmin;

        return new DeviceStatusRow(
            isAdmin ? device.Id : DevicePublicKey.For(device.Id),
            device.Platform,
            device.Label,
            isAdmin ? device.Target : null,
            isAdmin ? device.Package : null,
            probe?.Reachable == true,
            probe?.AppVersion,
            probe?.Build,
            ClientVersion(device, probe, inputs),
            storeLatest,
            StoreAheadCheck.IsAhead(storeLatest, probe?.AppVersion),
            LiveResult(device, probe, inputs.Versions),
            isAdmin ? probe?.Message : null,
            probe?.StartedAt,
            !isAdmin || update is null
                ? null
                : new DeviceUpdateSummary(update.Outcome, update.Message, update.Trigger, update.StartedAt),
            isAdmin ? inputs.CapturePortFor(device.Id) : 0);
    }

    private static string LiveResult(Device device, DeviceJobRow? probe, DeviceVersionIndex versions) {
        if (probe?.Reachable != true || string.IsNullOrEmpty(probe.AppVersion)) return probe?.Outcome ?? "";
        return DeviceProbeRunner.Classify(
            new DeviceProbeResult(true, probe.AppVersion, probe.Build, null),
            device.Platform, versions.LatestBuild(device.Platform), versions.LatestAppVersion(device.Platform));
    }

    private static int? ClientVersion(Device device, DeviceJobRow? probe, DeviceStatusInputs inputs) {
        if (inputs.Binaries?.CachedClientVersion(device.Platform, probe?.AppVersion) is { } cached) return cached;
        if (inputs.Versions.ClientVersion(device.Platform, probe?.AppVersion, probe?.Build) is { } extracted)
            return extracted;
        return inputs.CapturedClientVersions.TryGetValue(device.Id, out int captured) ? captured : null;
    }
}
