
namespace EggIncognito.Core.Services.Devices;

public interface IDevicePlatform {
    string Platform { get; }
    DeviceCapabilities Capabilities { get; }

    Task<DeviceResult<byte[]>> PullAppBinaryAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceResult<byte[]>> ReadAssetAsync(DeviceTarget target, DeviceAssetKind kind, string name,
        CancellationToken ct);
    Task<DeviceResult<IReadOnlyList<string>>> ListAssetsAsync(DeviceTarget target, DeviceAssetKind kind,
        CancellationToken ct);

    IReadOnlyList<HarvestEntry> Manifest();
    Task<DeviceResult<string>> FingerprintAsync(DeviceTarget target, HarvestEntry entry, CancellationToken ct);
    Task<DeviceResult<HarvestBatch>> HarvestAsync(DeviceTarget target, HarvestEntry entry,
        IReadOnlyDictionary<string, string> known, CancellationToken ct);

    Task<DeviceProbeResult> ProbeAsync(DeviceTarget target, CancellationToken ct);
    Task<StoreCheckResult> DriveStoreUpdateAsync(DeviceTarget target, CancellationToken ct,
        Action<string>? progress = null);

    Task<DeviceResult> SetProxyAsync(DeviceTarget target, string hostIp, int port, CancellationToken ct);
    Task<DeviceResult> ClearProxyAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceResult> InstallCaAsync(DeviceTarget target, string caPath, CancellationToken ct);

    Task<DeviceResult<UiTree>> DumpUiAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceResult<byte[]>> ScreenshotAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceResult<UiScreenSize>> ScreenSizeAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceResult> TapUiAsync(DeviceTarget target, UiSelector selector, CancellationToken ct);
    Task<DeviceResult> TapPointAsync(DeviceTarget target, int x, int y, CancellationToken ct);
    Task<DeviceResult> SwipeAsync(DeviceTarget target, int x1, int y1, int x2, int y2, int durationMs,
        CancellationToken ct);
    Task<DeviceResult> InputTextAsync(DeviceTarget target, string text, CancellationToken ct);
    Task<DeviceResult> KeyAsync(DeviceTarget target, DeviceKey key, CancellationToken ct);
    Task<DeviceResult> LaunchAppAsync(DeviceTarget target, string appRef, CancellationToken ct);

    Task<DeviceResult> RestartAppAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceResult> LockAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceResult> UnlockAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceResult> KillAppAsync(DeviceTarget target, CancellationToken ct);

    Task<DeviceResult<ParticleCaptureModel.Model>> CaptureParticlesAsync(DeviceTarget target, string scriptBody,
        string? addrOffset, CancellationToken ct);
}

public interface IDevicePlatforms {
    IDevicePlatform For(string platform);
}

public sealed class DevicePlatforms : IDevicePlatforms {
    private readonly Dictionary<string, IDevicePlatform> _byPlatform;
    private readonly NullDevicePlatform _fallback = new();

    public DevicePlatforms(IEnumerable<IDevicePlatform> platforms) {
        _byPlatform = platforms.ToDictionary(p => p.Platform, StringComparer.OrdinalIgnoreCase);
    }

    public IDevicePlatform For(string platform) =>
        !string.IsNullOrEmpty(platform) && _byPlatform.TryGetValue(platform, out var p) ? p : _fallback;
}

public sealed class NullDevicePlatform : IDevicePlatform {
    public string Platform => "none";
    public DeviceCapabilities Capabilities => DeviceCapabilities.None;

    private static string Note(DeviceTarget t) => $"no handler for platform '{t.Platform}'";

    public Task<DeviceResult<byte[]>> PullAppBinaryAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult<byte[]>.Unsupported(Note(target)));

    public Task<DeviceResult<byte[]>> ReadAssetAsync(DeviceTarget target, DeviceAssetKind kind, string name,
        CancellationToken ct) => Task.FromResult(DeviceResult<byte[]>.Unsupported(Note(target)));

    public Task<DeviceResult<IReadOnlyList<string>>> ListAssetsAsync(DeviceTarget target, DeviceAssetKind kind,
        CancellationToken ct) => Task.FromResult(DeviceResult<IReadOnlyList<string>>.Unsupported(Note(target)));

    public IReadOnlyList<HarvestEntry> Manifest() => [];

    public Task<DeviceResult<string>> FingerprintAsync(DeviceTarget target, HarvestEntry entry,
        CancellationToken ct) => Task.FromResult(DeviceResult<string>.Unsupported(Note(target)));

    public Task<DeviceResult<HarvestBatch>> HarvestAsync(DeviceTarget target, HarvestEntry entry,
        IReadOnlyDictionary<string, string> known, CancellationToken ct) =>
        Task.FromResult(DeviceResult<HarvestBatch>.Unsupported(Note(target)));

    public Task<DeviceProbeResult> ProbeAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(new DeviceProbeResult(false, null, null, Note(target)));

    public Task<StoreCheckResult> DriveStoreUpdateAsync(DeviceTarget target, CancellationToken ct,
        Action<string>? progress = null) =>
        Task.FromResult(new StoreCheckResult(false, null, null, false, false, "unsupported", Note(target)));

    public Task<DeviceResult> SetProxyAsync(DeviceTarget target, string hostIp, int port, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> ClearProxyAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> InstallCaAsync(DeviceTarget target, string caPath, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult<UiTree>> DumpUiAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult<UiTree>.Unsupported(Note(target)));

    public Task<DeviceResult<byte[]>> ScreenshotAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult<byte[]>.Unsupported(Note(target)));

    public Task<DeviceResult<UiScreenSize>> ScreenSizeAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult<UiScreenSize>.Unsupported(Note(target)));

    public Task<DeviceResult> TapUiAsync(DeviceTarget target, UiSelector selector, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> TapPointAsync(DeviceTarget target, int x, int y, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> SwipeAsync(DeviceTarget target, int x1, int y1, int x2, int y2, int durationMs,
        CancellationToken ct) => Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> InputTextAsync(DeviceTarget target, string text, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> KeyAsync(DeviceTarget target, DeviceKey key, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> LaunchAppAsync(DeviceTarget target, string appRef, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> RestartAppAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> LockAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> UnlockAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> KillAppAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult<ParticleCaptureModel.Model>> CaptureParticlesAsync(DeviceTarget target, string scriptBody,
        string? addrOffset, CancellationToken ct) =>
        Task.FromResult(DeviceResult<ParticleCaptureModel.Model>.Unsupported(Note(target)));
}
