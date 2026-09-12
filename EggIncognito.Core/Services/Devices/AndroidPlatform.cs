using System.IO.Compression;
using EggIncognito.Core.Services.ProtoExtract;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Core.Services.Devices;

public sealed class AndroidPlatform(
    IProcessRunner runner,
    IConfiguration appConfig,
    IEnumerable<IDeviceStoreChecker> storeCheckers,
    IEnumerable<IDeviceProxyConfigurator> proxyConfigurators,
    IEnumerable<IDeviceCaInstaller> caInstallers,
    IEnumerable<IDeviceUiDriver> uiDrivers,
    IEnumerable<IScreenStreamSource> screenStreamSources,
    ILogger<AndroidPlatform> logger)
    : DevicePlatformBase(Platforms.Android, storeCheckers, proxyConfigurators, caInstallers, uiDrivers,
        screenStreamSources) {
    private readonly bool _particleCaptureEnabled =
        !bool.TryParse(appConfig["DeviceCapture:AndroidParticleCapture"], out bool enabled) || enabled;

    public override async Task<DeviceResult<byte[]>> PullAppBinaryAsync(DeviceTarget target, CancellationToken ct) {
        var puller = new DeviceApkPuller(runner);
        byte[]? apk = await puller.PullArmSplitAsync(target.Target, target.Package, ct)
                      ?? await puller.PullBaseSplitAsync(target.Target, target.Package, ct);
        if (apk is null) {
            logger.LogWarning("android: {Id} could not pull apk split for {Pkg}", target.Id, target.Package);
            return DeviceResult<byte[]>.Unreachable("no apk pulled (device offline or adb unavailable)");
        }

        byte[]? so = ExtractLibFromApk(apk);
        return so is null
            ? DeviceResult<byte[]>.Error("libegginc.so not found inside apk")
            : DeviceResult<byte[]>.Success(so);
    }

    public override async Task<DeviceResult<byte[]>> ReadAssetAsync(DeviceTarget target, DeviceAssetKind kind,
        string name, CancellationToken ct) {
        byte[]? apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(target.Target, target.Package, ct);
        byte[]? asset = apk is null
            ? null
            : kind switch {
                DeviceAssetKind.Mesh => RpoAssetLister.ReadStem(apk, name),
                DeviceAssetKind.Texture => ApkTextureLister.ReadStem(apk, name),
                _ => null
            };
        return asset is null
            ? DeviceResult<byte[]>.Error($"asset '{name}' ({kind}) not found (no apk or missing entry)")
            : DeviceResult<byte[]>.Success(asset);
    }

    public override async Task<DeviceResult<IReadOnlyList<string>>> ListAssetsAsync(DeviceTarget target,
        DeviceAssetKind kind, CancellationToken ct) {
        byte[]? apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(target.Target, target.Package, ct);
        if (apk is null) return DeviceResult<IReadOnlyList<string>>.Error("no apk pulled");
        IReadOnlyList<string> stems = kind switch {
            DeviceAssetKind.Mesh => RpoAssetLister.ListStems(apk),
            DeviceAssetKind.Texture => ApkTextureLister.ListStems(apk),
            _ => []
        };
        return DeviceResult<IReadOnlyList<string>>.Success(stems);
    }

    private const string AndroidBinaryName = "libegginc.so";
    private const string ManifestUnsupported = "probe owns android package metadata";

    public override IReadOnlyList<HarvestEntry> Manifest() => [
        new(HarvestEntries.AppBinary, DeviceAssetKinds.Binary),
        new(HarvestEntries.AppPackage, DeviceAssetKinds.Package),
        new(HarvestEntries.Meshes, DeviceAssetKinds.Mesh),
        new(HarvestEntries.Textures, DeviceAssetKinds.Icon),
        new(HarvestEntries.PackageManifest, DeviceAssetKinds.Manifest, false, ManifestUnsupported)
    ];

    public override async Task<DeviceResult<string>> FingerprintAsync(DeviceTarget target, HarvestEntry entry,
        CancellationToken ct) {
        if (!entry.Supported) return DeviceResult<string>.Unsupported(entry.UnsupportedNote);
        var conn = new AdbDeviceConnection(runner, target.Target);
        var r = await conn.ShellAsync(
            $"pm path {target.Package} | sed 's/^package://' | sort | xargs sha256sum 2>/dev/null", ct);
        if (r.ExitCode != 0 || r.Stdout.Trim().Length == 0)
            return DeviceResult<string>.Unreachable(DeviceParsing.TrimNote(r.Stderr + r.Stdout));
        return DeviceResult<string>.Success(Hashes.Sha256Hex(r.Stdout.Trim()));
    }

    public override async Task<DeviceResult<HarvestBatch>> HarvestAsync(DeviceTarget target, HarvestEntry entry,
        IReadOnlyDictionary<string, string> known, CancellationToken ct) {
        if (!entry.Supported) return DeviceResult<HarvestBatch>.Unsupported(entry.UnsupportedNote);
        var puller = new DeviceApkPuller(runner);

        if (entry.Name == HarvestEntries.AppBinary) {
            byte[]? apk = await puller.PullArmSplitAsync(target.Target, target.Package, ct)
                          ?? await puller.PullBaseSplitAsync(target.Target, target.Package, ct);
            if (apk is null) return DeviceResult<HarvestBatch>.Unreachable("no apk pulled");
            byte[]? so = ExtractLibFromApk(apk);
            return so is null
                ? DeviceResult<HarvestBatch>.Error("libegginc.so not found inside apk")
                : DeviceResult<HarvestBatch>.Success(
                    new HarvestBatch([new HarvestItem(AndroidBinaryName, so, "application/octet-stream")],
                        [AndroidBinaryName], true));
        }

        if (entry.Name == HarvestEntries.AppPackage) {
            byte[]? arm = await puller.PullArmSplitAsync(target.Target, target.Package, ct);
            byte[]? baseApk = await puller.PullBaseSplitAsync(target.Target, target.Package, ct);
            var parts = new List<HarvestItem>(2);
            if (arm is not null)
                parts.Add(new HarvestItem(HarvestEntries.AndroidArmSplit, arm, "application/vnd.android.package-archive"));
            if (baseApk is not null)
                parts.Add(new HarvestItem(HarvestEntries.AndroidBaseSplit, baseApk, "application/vnd.android.package-archive"));
            return parts.Count == 0
                ? DeviceResult<HarvestBatch>.Unreachable("no apk splits pulled")
                : DeviceResult<HarvestBatch>.Success(
                    new HarvestBatch(parts, [.. parts.Select(p => p.Name)], true));
        }

        byte[]? bas = await puller.PullBaseSplitAsync(target.Target, target.Package, ct);
        if (bas is null) return DeviceResult<HarvestBatch>.Unreachable("no base apk pulled");

        return entry.Name switch {
            HarvestEntries.Meshes => DeviceResult<HarvestBatch>.Success(
                Collect(RpoAssetLister.ListStems(bas), s => RpoAssetLister.ReadStem(bas, s),
                    "application/octet-stream")),
            HarvestEntries.Textures => DeviceResult<HarvestBatch>.Success(
                Collect(ApkTextureLister.ListStems(bas), s => ApkTextureLister.ReadStem(bas, s), "image/png")),
            _ => DeviceResult<HarvestBatch>.Unsupported($"unknown harvest entry '{entry.Name}'")
        };
    }

    private static HarvestBatch Collect(IReadOnlyList<string> stems, Func<string, byte[]?> read, string contentType) {
        var items = new List<HarvestItem>(stems.Count);
        int failed = 0;
        foreach (string stem in stems) {
            if (read(stem) is { } bytes) items.Add(new HarvestItem(stem, bytes, contentType));
            else failed++;
        }

        return new HarvestBatch(items, stems, true, failed);
    }

    public override Task<DeviceProbeResult> ProbeAsync(DeviceTarget target, CancellationToken ct) =>
        new AdbDeviceProbe(runner, target.Target, target.Package).ProbeAsync(ct);

    public override async Task<DeviceResult> RestartAppAsync(DeviceTarget target, CancellationToken ct) {
        try {
            var conn = new AdbDeviceConnection(runner, target.Target);
            await conn.ShellAsync("input keyevent KEYCODE_WAKEUP", ct);
            await conn.ShellAsync("wm dismiss-keyguard", ct);
            await conn.ShellAsync("svc power stayon true", ct);
            var stop = await conn.ShellAsync($"am force-stop {target.Package}", ct);
            if (stop.ExitCode != 0) {
                logger.LogWarning("android: {Id} force-stop failed: {Note}",
                    target.Id, DeviceParsing.TrimNote(stop.Stderr + stop.Stdout));
            }

            var launch = await conn.ShellAsync(
                $"monkey -p {target.Package} -c android.intent.category.LAUNCHER 1", ct);
            return launch.ExitCode == 0 ? DeviceResult.Success("restarted") : DeviceResult.Error("launch failed");
        } catch (Exception ex) {
            logger.LogDebug(ex, "android: {Id} restart failed (non-fatal)", target.Id);
            return DeviceResult.Error(ex.Message);
        }
    }

    public override async Task<DeviceResult> LockAsync(DeviceTarget target, CancellationToken ct) {
        var conn = new AdbDeviceConnection(runner, target.Target);
        await conn.ShellAsync("svc power stayon false", ct);
        var r = await conn.ShellAsync("input keyevent KEYCODE_SLEEP", ct);
        return r.ExitCode == 0 ? DeviceResult.Success("locked") : DeviceResult.Error("lock failed");
    }

    public override async Task<DeviceResult> UnlockAsync(DeviceTarget target, CancellationToken ct) {
        var conn = new AdbDeviceConnection(runner, target.Target);
        await conn.ShellAsync("input keyevent KEYCODE_WAKEUP", ct);
        var r = await conn.ShellAsync("wm dismiss-keyguard", ct);
        return r.ExitCode == 0 ? DeviceResult.Success("unlocked") : DeviceResult.Error("unlock failed");
    }

    public override async Task<DeviceResult> KillAppAsync(DeviceTarget target, CancellationToken ct) {
        var conn = new AdbDeviceConnection(runner, target.Target);
        var r = await conn.ShellAsync($"am force-stop {target.Package}", ct);
        return r.ExitCode == 0 ? DeviceResult.Success("killed") : DeviceResult.Error("force-stop failed");
    }

    public override async Task<DeviceResult<ParticleCaptureModel.Model>> CaptureParticlesAsync(DeviceTarget target,
        string scriptBody, string? addrOffset, CancellationToken ct) {
        if (!_particleCaptureEnabled)
            return DeviceResult<ParticleCaptureModel.Model>.Unsupported("android particle capture disabled by config");

        var conn = new AdbDeviceConnection(runner, target.Target);
        var model = await new AndroidParticleCapturer(conn, target.Package, scriptBody, addrOffset).CaptureAsync(ct);
        return model is { } m
            ? DeviceResult<ParticleCaptureModel.Model>.Success(m)
            : DeviceResult<ParticleCaptureModel.Model>.Error("frida capture failed or frida-server absent");
    }

    private static byte[]? ExtractLibFromApk(byte[] apk) {
        if (apk.Length == 0) return null;
        ZipArchive zip;
        try {
            zip = new ZipArchive(new MemoryStream(apk, false), ZipArchiveMode.Read);
        } catch {
            return null;
        }

        using (zip) {
            var arm64 = zip.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith("/libegginc.so", StringComparison.OrdinalIgnoreCase)
                && e.FullName.Contains("arm64-v8a", StringComparison.OrdinalIgnoreCase));
            var chosen = arm64 ?? zip.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith("/libegginc.so", StringComparison.OrdinalIgnoreCase));
            if (chosen is null) return null;
            using var es = chosen.Open();
            using var buf = new MemoryStream();
            es.CopyTo(buf);
            return buf.ToArray();
        }
    }
}
