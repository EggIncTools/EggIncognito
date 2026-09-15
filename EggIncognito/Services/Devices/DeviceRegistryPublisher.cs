using EggIncognito.Core;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices;

public enum PublishOutcome {
    Published,
    NotConfigured,
    UnknownDevice,
    UnsupportedPlatform,
    NotHarvested,
    StaleHarvest,
    MissingAsset,
    CarveFailed,
    WriteFailed
}

public sealed record PublishResult(
    PublishOutcome Outcome,
    string? AppVersion = null,
    string? Build = null,
    string? ProtoSha = null,
    bool Created = false,
    string? Error = null);

public sealed class DeviceRegistryPublisher(
    IServiceProvider services,
    ILogger<DeviceRegistryPublisher> logger) {
    private const string PlatformAndroid = "android";
    private const string PlatformIos = "ios";

    public async Task<PublishResult> PublishAsync(string deviceId, string attribution, bool pokeWhenStale,
        CancellationToken ct) {
        if (services.GetService(typeof(IDeviceStatusStore)) is not IDeviceStatusStore store
            || services.GetService(typeof(ProtoRegistryStore)) is not ProtoRegistryStore registry
            || services.GetService(typeof(DeviceStateStore)) is not DeviceStateStore states)
            return new PublishResult(PublishOutcome.NotConfigured, Error: "no database configured");

        var device = await store.GetAsync(deviceId, ct);
        if (device is null) return new PublishResult(PublishOutcome.UnknownDevice, Error: "unknown device");
        if (device.Platform is not (PlatformAndroid or PlatformIos))
            return new PublishResult(PublishOutcome.UnsupportedPlatform,
                Error: $"no extractor for platform {device.Platform}");

        var state = await states.GetAsync(deviceId, ct);
        if (state is null || string.IsNullOrEmpty(state.AppVersion))
            return new PublishResult(PublishOutcome.NotHarvested,
                Error: "device has not been harvested yet; poke the device agent and retry");

        if (await StaleAsync(deviceId, state, pokeWhenStale, ct) is { } stale) return stale;

        var carve = await CarveAsync(device, state, ct);
        if (carve.Error is not null) return carve.Error;

        string appVersion = state.AppVersion;
        string build = carve.Result!.Build;
        string sha = carve.Result.ProtoSha ?? Hashes.Sha256Hex(carve.Result.Proto);
        string? clientVersion = carve.Result.ClientVersion?.ToString() ?? state.ClientVersion?.ToString();

        try {
            var upsert = await registry.UpsertAsync(
                device.Platform, appVersion, build, clientVersion, device.Package,
                sha, $"device:{device.Id}", DateTimeOffset.UtcNow,
                attribution, carve.Result.Proto, "device", true, ct);
            logger.LogInformation(
                "device publish: {Id} -> registry {Plat} build {Build} ({State}, sha {Sha}, by {Who})",
                deviceId, device.Platform, build, upsert.Created ? "created" : "updated", sha[..12], attribution);
            return new PublishResult(PublishOutcome.Published, appVersion, build, sha, upsert.Created);
        } catch (Exception ex) {
            logger.LogError(ex, "device publish: {Id} registry upsert failed for build {Build}", deviceId, build);
            return new PublishResult(PublishOutcome.WriteFailed, appVersion, build,
                Error: $"registry write failed: {ex.Message}");
        }
    }

    public async Task<bool> InRegistryAsync(string platform, string? build, CancellationToken ct) {
        if (string.IsNullOrEmpty(build)) return false;
        if (services.GetService(typeof(ProtoRegistryStore)) is not ProtoRegistryStore registry) return false;
        return await registry.GetAsync(platform, build, ct) is not null;
    }

    private async Task<PublishResult?> StaleAsync(string id, DeviceState state, bool poke, CancellationToken ct) {
        if (services.GetService(typeof(DeviceTimelineCache)) is not DeviceTimelineCache timeline) return null;
        var probe = await timeline.LatestAsync(id, DeviceJobKinds.Probe, ct);
        if (probe is not { Reachable: true } || string.IsNullOrEmpty(probe.Build)) return null;
        if (string.Equals(probe.Build, state.Build, StringComparison.Ordinal)) return null;

        bool poked = false;
        if (poke && services.GetService(typeof(IDeviceAgentClient)) is IDeviceAgentClient { Enabled: true } agent)
            poked = await agent.PokeAsync(id, true, ct);

        logger.LogWarning(
            "device publish: {Id} refused, harvest is {Harvested} but device runs {Installed} (poked={Poked})",
            id, state.Build ?? "?", probe.Build, poked);
        return new PublishResult(PublishOutcome.StaleHarvest, Error:
            $"harvest is stale: harvested build {state.Build ?? "none"}, device runs " +
            $"{probe.Build} ({probe.AppVersion ?? "?"})" +
            (poked ? "; poked the device agent, retry once the harvest lands" : "; poke the device agent and retry"));
    }

    private async Task<(Carve? Result, PublishResult? Error)> CarveAsync(
        Device device, DeviceState state, CancellationToken ct) {
        if (services.GetService(typeof(DeviceAssetStore)) is not DeviceAssetStore assets)
            return (null, new PublishResult(PublishOutcome.NotConfigured, Error: "no database configured"));

        if (device.Platform == PlatformAndroid) {
            var row = await assets.GetAsync(DeviceAssetKinds.Package, HarvestEntries.AndroidArmSplit,
                device.Platform, ct);
            if (row is null)
                return (null, new PublishResult(PublishOutcome.MissingAsset,
                    Error: "no harvested arm split for this device; poke the device agent and retry"));

            var carved = ArchiveProtoExtractor.Extract(await assets.BytesAsync(row, ct));
            if (!carved.Ok || string.IsNullOrEmpty(carved.Proto)) {
                logger.LogWarning("device publish: {Id} carve failed ({Diag})", device.Id, carved.Diagnostics);
                return (null, new PublishResult(PublishOutcome.CarveFailed,
                    Error: $"proto carve failed: {carved.Diagnostics}"));
            }

            if (string.IsNullOrEmpty(state.Build))
                return (null, new PublishResult(PublishOutcome.NotHarvested,
                    Error: "harvested state has no android build number"));

            return (new Carve(carved.Proto, state.Build, carved.ClientVersion, carved.ProtoSha), null);
        }

        var binaries = (GameBinaryProvider)services.GetRequiredService(typeof(GameBinaryProvider));
        var bin = await binaries.GetExtractionBinaryAsync(device.Platform, ct);
        if (!bin.Ok || bin.Bytes is null)
            return (null, new PublishResult(PublishOutcome.MissingAsset,
                Error: $"no harvested {device.Platform} binary: {bin.Diagnostics}"));

        if (!string.IsNullOrEmpty(state.AppVersion) &&
            !string.Equals(bin.Version, state.AppVersion, StringComparison.Ordinal)) {
            logger.LogWarning("device publish: {Id} refused, binary is {BinVersion} but harvest state is {State}",
                device.Id, bin.Version, state.AppVersion);
            return (null, new PublishResult(PublishOutcome.StaleHarvest,
                Error: $"harvested {device.Platform} binary is {bin.Version}, device reports {state.AppVersion}; " +
                       $"poke the device agent and retry once the {state.AppVersion} binary lands ({bin.Diagnostics})"));
        }

        var carveIos = MachoProtoExtractor.Extract(bin.Bytes);
        if (!carveIos.Ok || string.IsNullOrEmpty(carveIos.Proto)) {
            logger.LogWarning("device publish: {Id} carve failed ({Diag})", device.Id, carveIos.Diagnostics);
            return (null, new PublishResult(PublishOutcome.CarveFailed,
                Error: $"proto carve failed: {carveIos.Diagnostics}"));
        }

        string build = !string.IsNullOrEmpty(state.Build) ? state.Build : Hashes.Sha256HexShort(bin.Bytes, 16);
        return (new Carve(carveIos.Proto, build, LibegincClientVersion.ReadFromBinary(bin.Bytes), carveIos.ProtoSha),
            null);
    }

    private sealed record Carve(string Proto, string Build, int? ClientVersion, string? ProtoSha);
}
