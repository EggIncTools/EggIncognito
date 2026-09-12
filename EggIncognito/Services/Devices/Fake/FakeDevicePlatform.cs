using EggIncognito.Core;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Fake;

public sealed class FakeDevicePlatform(
    string platform,
    FakeDeviceSettings settings,
    FakeDeviceVersions versions,
    FakeFixtureSource fixtures,
    ILogger<FakeDevicePlatform> logger,
    IEnumerable<IDeviceStoreChecker> storeCheckers,
    IEnumerable<IDeviceProxyConfigurator> proxyConfigurators,
    IEnumerable<IDeviceCaInstaller> caInstallers,
    IEnumerable<IDeviceUiDriver> uiDrivers,
    IEnumerable<IScreenStreamSource> screenStreamSources)
    : DevicePlatformBase(platform, storeCheckers, proxyConfigurators, caInstallers, uiDrivers,
        screenStreamSources) {
    private const string PackageUnsupported = "ios ships the app binary, not an installable package";
    private const string ManifestUnsupported = "probe owns android package metadata";
    private const string UnknownDevice = "not a declared fake device";
    private const string OfflineNote = "fake device is unreachable by scenario";
    private const string ParticleNote = "particle capture has no fixture";
    private const int ActionDelayMs = 120;

    public override DeviceCapabilities Capabilities =>
        DeviceCapabilities.BinaryPull | DeviceCapabilities.AssetRead | DeviceCapabilities.Probe |
        DeviceCapabilities.StoreUpdate | DeviceCapabilities.Proxy | DeviceCapabilities.CaInstall |
        DeviceCapabilities.AppLifecycle;

    public override IReadOnlyList<HarvestEntry> Manifest() {
        if (Platforms.Matches(Platform, Platforms.Android)) {
            return [
                new HarvestEntry(HarvestEntries.AppBinary, DeviceAssetKinds.Binary),
                new HarvestEntry(HarvestEntries.AppPackage, DeviceAssetKinds.Package),
                new HarvestEntry(HarvestEntries.Meshes, DeviceAssetKinds.Mesh),
                new HarvestEntry(HarvestEntries.Textures, DeviceAssetKinds.Icon),
                new HarvestEntry(HarvestEntries.PackageManifest, DeviceAssetKinds.Manifest, false,
                    ManifestUnsupported)
            ];
        }

        return [
            new HarvestEntry(HarvestEntries.AppBinary, DeviceAssetKinds.Binary),
            new HarvestEntry(HarvestEntries.AppPackage, DeviceAssetKinds.Package, false, PackageUnsupported),
            new HarvestEntry(HarvestEntries.Meshes, DeviceAssetKinds.Mesh),
            new HarvestEntry(HarvestEntries.Textures, DeviceAssetKinds.Icon),
            new HarvestEntry(HarvestEntries.PackageManifest, DeviceAssetKinds.Manifest)
        ];
    }

    public override async Task<DeviceProbeResult> ProbeAsync(DeviceTarget target, CancellationToken ct) {
        if (Resolve(target) is not { } device) return new DeviceProbeResult(false, null, null, UnknownDevice);
        await DelayAsync(device.ProbeDelayMs, ct);
        if (device.Scenario == FakeScenarios.Unreachable)
            return new DeviceProbeResult(false, null, null, OfflineNote);

        var installed = await InstalledAsync(device, ct);
        return new DeviceProbeResult(true, installed.AppVersion, installed.Build,
            $"fake device, scenario {device.Scenario}");
    }

    public override async Task<DeviceResult<string>> FingerprintAsync(DeviceTarget target, HarvestEntry entry,
        CancellationToken ct) {
        if (!entry.Supported) return DeviceResult<string>.Unsupported(entry.UnsupportedNote);
        if (Resolve(target) is not { } device) return DeviceResult<string>.Unreachable(UnknownDevice);
        if (device.Scenario == FakeScenarios.Unreachable) return DeviceResult<string>.Unreachable(OfflineNote);

        var installed = await InstalledAsync(device, ct);
        var set = await fixtures.DescribeAsync(device, entry, installed.AppVersion, ct);
        return DeviceResult<string>.Success($"{set.Tier}:{Hashes.Sha256Hex(set.Canonical(installed.AppVersion))}");
    }

    public override async Task<DeviceResult<HarvestBatch>> HarvestAsync(DeviceTarget target, HarvestEntry entry,
        IReadOnlyDictionary<string, string> known, CancellationToken ct) {
        if (!entry.Supported) return DeviceResult<HarvestBatch>.Unsupported(entry.UnsupportedNote);
        if (Resolve(target) is not { } device) return DeviceResult<HarvestBatch>.Unreachable(UnknownDevice);
        if (device.Scenario == FakeScenarios.Unreachable) return DeviceResult<HarvestBatch>.Unreachable(OfflineNote);

        if (device.Scenario == FakeScenarios.SlowHarvest && entry.Name == HarvestEntries.Meshes) {
            logger.LogInformation("fake device: {Id} holding entry {Entry} for {Ms}ms", device.Id, entry.Name,
                settings.SlowEntryMs);
            await DelayAsync(settings.SlowEntryMs, ct);
        }

        if (device.Scenario == FakeScenarios.FailingEntry && entry.Name == HarvestEntries.Textures)
            return DeviceResult<HarvestBatch>.Error($"fake fixture refuses entry {entry.Name} by scenario");

        var installed = await InstalledAsync(device, ct);
        var set = await fixtures.DescribeAsync(device, entry, installed.AppVersion, ct);
        var items = new List<HarvestItem>();
        foreach (var file in set.Files) {
            if (known.TryGetValue(file.Name, out string? have)
                && string.Equals(have, file.Sha256, StringComparison.Ordinal)) {
                continue;
            }

            byte[]? bytes = await fixtures.ReadAsync(device, entry, file.Name, installed.AppVersion, set.Tier, ct);
            if (bytes is null) continue;
            items.Add(new HarvestItem(file.Name, bytes, file.ContentType));
        }

        logger.LogInformation("fake device: {Id} entry {Entry} tier {Tier}, {Moved} of {Total} files moved",
            device.Id, entry.Name, set.Tier, items.Count, set.Files.Count);
        return DeviceResult<HarvestBatch>.Success(
            new HarvestBatch(items, [.. set.Files.Select(f => f.Name)], true));
    }

    public override async Task<DeviceResult<byte[]>> PullAppBinaryAsync(DeviceTarget target, CancellationToken ct) {
        if (Resolve(target) is not { } device) return DeviceResult<byte[]>.Unreachable(UnknownDevice);
        if (device.Scenario == FakeScenarios.Unreachable) return DeviceResult<byte[]>.Unreachable(OfflineNote);

        var entry = new HarvestEntry(HarvestEntries.AppBinary, DeviceAssetKinds.Binary);
        var installed = await InstalledAsync(device, ct);
        byte[]? bytes = await fixtures.ReadAsync(device, entry, FakeFixtureSource.BinaryName(device.Platform),
            installed.AppVersion, ct);
        return bytes is null
            ? DeviceResult<byte[]>.Error("no fake binary fixture available")
            : DeviceResult<byte[]>.Success(bytes);
    }

    public override async Task<DeviceResult<byte[]>> ReadAssetAsync(DeviceTarget target, DeviceAssetKind kind,
        string name, CancellationToken ct) {
        if (Resolve(target) is not { } device) return DeviceResult<byte[]>.Unreachable(UnknownDevice);
        if (device.Scenario == FakeScenarios.Unreachable) return DeviceResult<byte[]>.Unreachable(OfflineNote);

        var installed = await InstalledAsync(device, ct);
        byte[]? bytes = await fixtures.ReadAsync(device, EntryFor(kind), name, installed.AppVersion, ct);
        return bytes is null
            ? DeviceResult<byte[]>.Error($"no fake fixture for {kind} '{name}'")
            : DeviceResult<byte[]>.Success(bytes);
    }

    public override async Task<DeviceResult<IReadOnlyList<string>>> ListAssetsAsync(DeviceTarget target,
        DeviceAssetKind kind, CancellationToken ct) {
        if (Resolve(target) is not { } device)
            return DeviceResult<IReadOnlyList<string>>.Unreachable(UnknownDevice);
        if (device.Scenario == FakeScenarios.Unreachable)
            return DeviceResult<IReadOnlyList<string>>.Unreachable(OfflineNote);

        var installed = await InstalledAsync(device, ct);
        var names = await fixtures.ListAsync(device, EntryFor(kind), installed.AppVersion, ct);
        return DeviceResult<IReadOnlyList<string>>.Success(names);
    }

    public override Task<DeviceResult> RestartAppAsync(DeviceTarget target, CancellationToken ct) =>
        ActAsync(target, "restarted", ct);

    public override Task<DeviceResult> LockAsync(DeviceTarget target, CancellationToken ct) =>
        ActAsync(target, "locked", ct);

    public override Task<DeviceResult> UnlockAsync(DeviceTarget target, CancellationToken ct) =>
        ActAsync(target, "unlocked", ct);

    public override Task<DeviceResult> KillAppAsync(DeviceTarget target, CancellationToken ct) =>
        ActAsync(target, "killed", ct);

    public override Task<DeviceResult<ParticleCaptureModel.Model>> CaptureParticlesAsync(DeviceTarget target,
        string scriptBody, string? addrOffset, CancellationToken ct) =>
        Task.FromResult(DeviceResult<ParticleCaptureModel.Model>.Unsupported(ParticleNote));

    public override Task<DeviceResult<UiTree>> DumpUiAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult<UiTree>.Success(new UiTree(
            new UiNode(null, "fake", null, "android.widget.TextView", target.Package,
                new UiBounds(0, 0, 100, 100), true, true, []),
            "<hierarchy/>")));

    public override Task<DeviceResult<byte[]>> ScreenshotAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult<byte[]>.Success([]));

    public override Task<DeviceResult> TapUiAsync(DeviceTarget target, UiSelector selector, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Success("fake ui tap"));

    public override Task<DeviceResult> TapPointAsync(DeviceTarget target, int x, int y, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Success("fake ui tap point"));

    public override Task<DeviceResult> InputTextAsync(DeviceTarget target, string text, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Success("fake ui input text"));

    public override Task<DeviceResult> KeyAsync(DeviceTarget target, DeviceKey key, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Success($"fake ui key {key}"));

    public override Task<DeviceResult> LaunchAppAsync(DeviceTarget target, string appRef, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Success($"fake ui launch {appRef}"));

    private async Task<DeviceResult> ActAsync(DeviceTarget target, string verb, CancellationToken ct) {
        if (Resolve(target) is not { } device) return DeviceResult.Unreachable(UnknownDevice);
        if (device.Scenario == FakeScenarios.Unreachable) return DeviceResult.Unreachable(OfflineNote);
        await DelayAsync(ActionDelayMs, ct);
        return DeviceResult.Success($"fake device {verb}");
    }

    private static HarvestEntry EntryFor(DeviceAssetKind kind) =>
        kind == DeviceAssetKind.Mesh
            ? new HarvestEntry(HarvestEntries.Meshes, DeviceAssetKinds.Mesh)
            : new HarvestEntry(HarvestEntries.Textures, DeviceAssetKinds.Icon);

    private Task<FakeInstalledVersion> InstalledAsync(FakeDevice device, CancellationToken ct) =>
        fixtures.ResolveAsync(device, versions, ct);

    private FakeDevice? Resolve(DeviceTarget target) => settings.For(target.Id);

    private static Task DelayAsync(int milliseconds, CancellationToken ct) =>
        milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds, ct);
}
