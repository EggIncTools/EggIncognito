using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace EggIncognito.Tests.Devices;

public class PixelWatchServiceTests {
    private static readonly DeviceTarget Target = new("watch-test", "android", "127.0.0.1:5555", "com.auxbrain.egginc");

    private static byte[] Png() {
        using var image = new Image<Rgba32>(64, 64, new Rgba32(10, 10, 10));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static PixelWatchService NewService() => new(NullLogger<PixelWatchService>.Instance);

    [Fact]
    public async Task AddAsync_BeyondTheCap_ReportsTheLimit() {
        using var svc = NewService();
        var platform = new StubPlatform(Png());
        svc.SetClientWatching(Target.Id, true);

        for (int i = 0; i < PixelWatchService.MaxPoints; i++) {
            var ok = await svc.AddAsync(platform, Target, i, 0, CancellationToken.None);
            Assert.True(ok.Ok);
        }

        var over = await svc.AddAsync(platform, Target, 40, 0, CancellationToken.None);

        Assert.False(over.Ok);
        Assert.Equal("at most 32 watch points per device", over.Note);
        Assert.Equal(PixelWatchService.MaxPoints, svc.State(Target.Id).Points.Count);
    }

    [Fact]
    public async Task ClientWatching_StopsTheLoopFromScreenshotting() {
        using var svc = NewService();
        var platform = new StubPlatform(Png());
        svc.SetClientWatching(Target.Id, true);

        Assert.True((await svc.AddAsync(platform, Target, 1, 1, CancellationToken.None)).Ok);
        int afterArm = platform.Screenshots;
        await Task.Delay(PixelWatchService.Poll * 3);

        Assert.Equal(1, afterArm);
        Assert.Equal(afterArm, platform.Screenshots);
    }

    [Fact]
    public async Task HitAsync_TapsOnceThenCoolsDown() {
        using var svc = NewService();
        var platform = new StubPlatform(Png());
        svc.SetClientWatching(Target.Id, true);
        var added = await svc.AddAsync(platform, Target, 2, 2, CancellationToken.None);
        string point = added.Value!.Points[0].Id;
        await Task.Delay(PixelWatchService.AfterTap + TimeSpan.FromMilliseconds(150));

        int before = svc.State(Target.Id).Points[0].Taps;
        var hit = await svc.HitAsync(platform, Target, point, CancellationToken.None);
        int tapped = svc.State(Target.Id).Points[0].Taps;
        var again = await svc.HitAsync(platform, Target, point, CancellationToken.None);

        Assert.True(hit.Ok);
        Assert.True(tapped > before);
        Assert.Equal("cooling down", again.Note);
        Assert.Equal(tapped, svc.State(Target.Id).Points[0].Taps);
    }

    [Fact]
    public async Task HitAsync_UnknownPoint_Fails() {
        using var svc = NewService();
        var platform = new StubPlatform(Png());
        svc.SetClientWatching(Target.Id, true);
        await svc.AddAsync(platform, Target, 3, 3, CancellationToken.None);

        var hit = await svc.HitAsync(platform, Target, "nope", CancellationToken.None);

        Assert.False(hit.Ok);
        Assert.Equal("unknown watch point", hit.Note);
    }

    private sealed class StubPlatform(byte[] png) : DevicePlatformBase("android", [], [], [], [], []) {
        private const string Refusal = "stub platform";
        private int _screenshots;

        public int Screenshots => Volatile.Read(ref _screenshots);

        public override Task<DeviceResult<byte[]>> ScreenshotAsync(DeviceTarget target, CancellationToken ct) {
            Interlocked.Increment(ref _screenshots);
            return Task.FromResult(DeviceResult<byte[]>.Success(png));
        }

        public override Task<DeviceResult> TapPointAsync(DeviceTarget target, int x, int y, CancellationToken ct) =>
            Task.FromResult(DeviceResult.Success());

        public override Task<DeviceProbeResult> ProbeAsync(DeviceTarget target, CancellationToken ct) =>
            throw new NotSupportedException(Refusal);

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
