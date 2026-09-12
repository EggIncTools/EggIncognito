using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Devices;

public class DeviceProbeRunnerTests {
    private static Device Android => new() { Id = "a", Platform = "android", Target = "s", Package = "p" };
    private static Device Ios => new() { Id = "i", Platform = "ios", Target = "u", Package = "p" };

    [Fact]
    public void Classify_Unreachable() {
        var r = new DeviceProbeResult(false, null, null, "off");
        Assert.Equal("unreachable", DeviceProbeRunner.Classify(Android, r, "111344", "1.35.7"));
    }

    [Fact]
    public void Classify_PlatformStringOverload_MatchesDeviceOverload() {
        var r = new DeviceProbeResult(true, "1.36", "1.36.0.2", null);
        Assert.Equal("new_version", DeviceProbeRunner.Classify(r, "ios", null, "1.35.8"));
        Assert.Equal(DeviceProbeRunner.Classify(Ios, r, null, "1.35.8"),
            DeviceProbeRunner.Classify(r, "ios", null, "1.35.8"));
    }

    [Fact]
    public void Classify_Android_InstalledBuildAhead_NewVersion() {
        var r = new DeviceProbeResult(true, "1.35.7", "111344", null);
        Assert.Equal("new_version", DeviceProbeRunner.Classify(Android, r, "111340", "1.35.6"));
    }

    [Fact]
    public void Classify_Android_BuildEqual_NoChange() {
        var r = new DeviceProbeResult(true, "1.35.7", "111344", null);
        Assert.Equal("no_change", DeviceProbeRunner.Classify(Android, r, "111344", "1.35.7"));
    }

    [Fact]
    public void Classify_Android_NothingExtractedYet_NewVersion() {
        var r = new DeviceProbeResult(true, "1.35.7", "111344", null);
        Assert.Equal("new_version", DeviceProbeRunner.Classify(Android, r, null, null));
    }

    [Fact]
    public void Classify_Ios_InstalledSemverAhead_NewVersion() {
        var r = new DeviceProbeResult(true, "1.35.8", null, null);
        Assert.Equal("new_version", DeviceProbeRunner.Classify(Ios, r, null, "1.35.7"));
    }

    [Fact]
    public void Classify_Ios_SameSemver_NoChange() {
        var r = new DeviceProbeResult(true, "1.35.8", null, null);
        Assert.Equal("no_change", DeviceProbeRunner.Classify(Ios, r, null, "1.35.8"));
    }

    [Fact]
    public void Classify_ReachableButNoVersion_Error() {
        var r = new DeviceProbeResult(true, null, null, "app not installed");
        Assert.Equal("error", DeviceProbeRunner.Classify(Ios, r, null, "1.35.8"));
    }

    [Fact]
    public async Task ProbeOneAsync_DispatchesThroughDevicePlatforms() {
        var platforms = new RecordingPlatforms();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProbeAsync(Ios, platforms));

        Assert.Equal("ios", platforms.Asked);
        Assert.Equal("i", platforms.Target?.Id);
        Assert.Equal("u", platforms.Target?.Target);
        Assert.Equal("p", platforms.Target?.Package);
    }

    [Fact]
    public async Task ProbeOneAsync_AsksForTheDevicesOwnPlatform() {
        var platforms = new RecordingPlatforms();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProbeAsync(Android, platforms));
        Assert.Equal("android", platforms.Asked);
    }

    private static Task<DeviceJobRow> ProbeAsync(Device d, IDevicePlatforms platforms) {
        var db = new EggIncognitoDbContext(new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none").Options);
        return DeviceProbeRunner.ProbeOneAsync(d, "test", platforms, new DeviceJobStore(db, TimeProvider.System),
            db, NullLogger.Instance, TimeProvider.System, CancellationToken.None);
    }

    private sealed class RecordingPlatforms : IDevicePlatforms {
        private readonly RecordingPlatform _ios = new(Platforms.Ios);
        private readonly RecordingPlatform _android = new(Platforms.Android);

        public string? Asked { get; private set; }
        public DeviceTarget? Target => _ios.Target ?? _android.Target;

        public IDevicePlatform For(string platform) {
            Asked = platform;
            return Platforms.Matches(platform, Platforms.Ios) ? _ios : _android;
        }
    }

    private sealed class RecordingPlatform(string platform)
        : DevicePlatformBase(platform, [], [], [], [], []) {
        private const string Refusal = "recording platform";

        public DeviceTarget? Target { get; private set; }

        public override Task<DeviceProbeResult> ProbeAsync(DeviceTarget target, CancellationToken ct) {
            Target = target;
            throw new InvalidOperationException(Refusal);
        }

        public override Task<DeviceResult<byte[]>> PullAppBinaryAsync(DeviceTarget target, CancellationToken ct) =>
            throw new NotSupportedException(Refusal);

        public override Task<DeviceResult<byte[]>> ReadAssetAsync(DeviceTarget target, DeviceAssetKind kind,
            string name, CancellationToken ct) => throw new NotSupportedException(Refusal);

        public override Task<DeviceResult<IReadOnlyList<string>>> ListAssetsAsync(DeviceTarget target,
            DeviceAssetKind kind, CancellationToken ct) => throw new NotSupportedException(Refusal);

        public override IReadOnlyList<HarvestEntry> Manifest() => [];

        public override Task<DeviceResult<string>> FingerprintAsync(DeviceTarget target, HarvestEntry entry,
            CancellationToken ct) => throw new NotSupportedException(Refusal);

        public override Task<DeviceResult<HarvestBatch>> HarvestAsync(DeviceTarget target, HarvestEntry entry,
            IReadOnlyDictionary<string, string> known, CancellationToken ct) =>
            throw new NotSupportedException(Refusal);

        public override Task<DeviceResult> RestartAppAsync(DeviceTarget target, CancellationToken ct) =>
            throw new NotSupportedException(Refusal);

        public override Task<DeviceResult> LockAsync(DeviceTarget target, CancellationToken ct) =>
            throw new NotSupportedException(Refusal);

        public override Task<DeviceResult> UnlockAsync(DeviceTarget target, CancellationToken ct) =>
            throw new NotSupportedException(Refusal);

        public override Task<DeviceResult> KillAppAsync(DeviceTarget target, CancellationToken ct) =>
            throw new NotSupportedException(Refusal);

        public override Task<DeviceResult<ParticleCaptureModel.Model>> CaptureParticlesAsync(DeviceTarget target,
            string scriptBody, string? addrOffset, CancellationToken ct) => throw new NotSupportedException(Refusal);
    }
}
