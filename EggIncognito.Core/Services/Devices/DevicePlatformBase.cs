
namespace EggIncognito.Core.Services.Devices;

public abstract class DevicePlatformBase : IDevicePlatform {
    protected DevicePlatformBase(
        string platform,
        IEnumerable<IDeviceStoreChecker> storeCheckers,
        IEnumerable<IDeviceProxyConfigurator> proxyConfigurators,
        IEnumerable<IDeviceCaInstaller> caInstallers,
        IEnumerable<IDeviceUiDriver> uiDrivers) {
        Platform = platform;
        Store = storeCheckers.FirstOrDefault(c => Platforms.Matches(c.Platform, platform));
        Proxy = proxyConfigurators.FirstOrDefault(c => Platforms.Matches(c.Platform, platform));
        Ca = caInstallers.FirstOrDefault(c => Platforms.Matches(c.Platform, platform));
        Ui = uiDrivers.FirstOrDefault(c => Platforms.Matches(c.Platform, platform));
    }

    protected IDeviceStoreChecker? Store { get; }
    protected IDeviceProxyConfigurator? Proxy { get; }
    protected IDeviceCaInstaller? Ca { get; }
    protected IDeviceUiDriver? Ui { get; }

    public string Platform { get; }

    public virtual DeviceCapabilities Capabilities =>
        DeviceCapabilities.BinaryPull | DeviceCapabilities.AssetRead | DeviceCapabilities.Probe |
        DeviceCapabilities.StoreUpdate | DeviceCapabilities.Proxy | DeviceCapabilities.CaInstall |
        DeviceCapabilities.AppLifecycle | DeviceCapabilities.ParticleCapture |
        (Ui is not null ? DeviceCapabilities.UiNavigation : DeviceCapabilities.None);

    public Task<StoreCheckResult> DriveStoreUpdateAsync(DeviceTarget target, CancellationToken ct,
        Action<string>? progress = null) =>
        Store is null
            ? Task.FromResult(new StoreCheckResult(false, null, null, false, false, "unsupported",
                $"no {Platform} store checker"))
            : Store.CheckAndUpdateAsync(target, ct, progress);

    public async Task<DeviceResult> SetProxyAsync(DeviceTarget target, string hostIp, int port, CancellationToken ct) =>
        Proxy is null
            ? DeviceResult.Unsupported($"no {Platform} proxy configurator")
            : DeviceResult.From(await Proxy.SetProxyAsync(target, hostIp, port, ct));

    public async Task<DeviceResult> ClearProxyAsync(DeviceTarget target, CancellationToken ct) =>
        Proxy is null
            ? DeviceResult.Unsupported($"no {Platform} proxy configurator")
            : DeviceResult.From(await Proxy.ClearProxyAsync(target, ct));

    public async Task<DeviceResult> InstallCaAsync(DeviceTarget target, string caPath, CancellationToken ct) =>
        Ca is null
            ? DeviceResult.Unsupported($"no {Platform} ca installer")
            : DeviceResult.From(await Ca.InstallAsync(target, caPath, ct));

    public virtual async Task<DeviceResult<UiTree>> DumpUiAsync(DeviceTarget target, CancellationToken ct) =>
        Ui is null
            ? DeviceResult<UiTree>.Unsupported($"no {Platform} ui driver")
            : await Ui.DumpAsync(target, ct);

    public virtual async Task<DeviceResult<byte[]>> ScreenshotAsync(DeviceTarget target, CancellationToken ct) =>
        Ui is null
            ? DeviceResult<byte[]>.Unsupported($"no {Platform} ui driver")
            : await Ui.ScreenshotAsync(target, ct);

    public virtual async Task<DeviceResult<UiScreenSize>> ScreenSizeAsync(DeviceTarget target, CancellationToken ct) =>
        Ui is null
            ? DeviceResult<UiScreenSize>.Unsupported($"no {Platform} ui driver")
            : await Ui.ScreenSizeAsync(target, ct);

    public virtual async Task<DeviceResult> TapUiAsync(DeviceTarget target, UiSelector selector, CancellationToken ct) =>
        Ui is null
            ? DeviceResult.Unsupported($"no {Platform} ui driver")
            : await Ui.TapAsync(target, selector, ct);

    public virtual async Task<DeviceResult> TapPointAsync(DeviceTarget target, int x, int y, CancellationToken ct) =>
        Ui is null
            ? DeviceResult.Unsupported($"no {Platform} ui driver")
            : await Ui.TapPointAsync(target, x, y, ct);

    public virtual async Task<DeviceResult> SwipeAsync(DeviceTarget target, int x1, int y1, int x2, int y2,
        int durationMs, CancellationToken ct) =>
        Ui is null
            ? DeviceResult.Unsupported($"no {Platform} ui driver")
            : await Ui.SwipeAsync(target, x1, y1, x2, y2, durationMs, ct);

    public virtual async Task<DeviceResult> InputTextAsync(DeviceTarget target, string text, CancellationToken ct) =>
        Ui is null
            ? DeviceResult.Unsupported($"no {Platform} ui driver")
            : await Ui.InputTextAsync(target, text, ct);

    public virtual async Task<DeviceResult> KeyAsync(DeviceTarget target, DeviceKey key, CancellationToken ct) =>
        Ui is null
            ? DeviceResult.Unsupported($"no {Platform} ui driver")
            : await Ui.KeyAsync(target, key, ct);

    public virtual async Task<DeviceResult> LaunchAppAsync(DeviceTarget target, string appRef, CancellationToken ct) =>
        Ui is null
            ? DeviceResult.Unsupported($"no {Platform} ui driver")
            : await Ui.LaunchAppAsync(target, appRef, ct);

    public abstract Task<DeviceResult<byte[]>> PullAppBinaryAsync(DeviceTarget target, CancellationToken ct);

    public abstract Task<DeviceResult<byte[]>> ReadAssetAsync(DeviceTarget target, DeviceAssetKind kind, string name,
        CancellationToken ct);

    public abstract Task<DeviceResult<IReadOnlyList<string>>> ListAssetsAsync(DeviceTarget target, DeviceAssetKind kind,
        CancellationToken ct);

    public abstract IReadOnlyList<HarvestEntry> Manifest();

    public abstract Task<DeviceResult<string>> FingerprintAsync(DeviceTarget target, HarvestEntry entry,
        CancellationToken ct);

    public abstract Task<DeviceResult<HarvestBatch>> HarvestAsync(DeviceTarget target, HarvestEntry entry,
        IReadOnlyDictionary<string, string> known, CancellationToken ct);

    public abstract Task<DeviceProbeResult> ProbeAsync(DeviceTarget target, CancellationToken ct);
    public abstract Task<DeviceResult> RestartAppAsync(DeviceTarget target, CancellationToken ct);
    public abstract Task<DeviceResult> LockAsync(DeviceTarget target, CancellationToken ct);
    public abstract Task<DeviceResult> UnlockAsync(DeviceTarget target, CancellationToken ct);
    public abstract Task<DeviceResult> KillAppAsync(DeviceTarget target, CancellationToken ct);

    public abstract Task<DeviceResult<ParticleCaptureModel.Model>> CaptureParticlesAsync(DeviceTarget target,
        string scriptBody, string? addrOffset, CancellationToken ct);
}
