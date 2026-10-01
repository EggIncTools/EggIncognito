using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;
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

    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(20);

    private static PixelWatchService NewService(TimeProvider? time = null) =>
        new(NullLogger<PixelWatchService>.Instance, time ?? TimeProvider.System) { PollEvery = FastPoll };

    [Fact]
    public async Task AddAsync_BeyondTheCap_ReportsTheLimit() {
        using var svc = NewService();
        var platform = new StubPlatform(Png());
        svc.SetClientWatching(Target.Id, true);

        for (int i = 0; i < PixelWatchService.MaxPoints; i++) {
            var ok = await svc.AddAsync(platform, Target, new PixelWatchRequest(i, 0), CancellationToken.None);
            Assert.True(ok.Ok);
        }

        var over = await svc.AddAsync(platform, Target, new PixelWatchRequest(40, 0), CancellationToken.None);

        Assert.False(over.Ok);
        Assert.Equal("at most 32 watch points per device", over.Note);
        Assert.Equal(PixelWatchService.MaxPoints, svc.State(Target.Id).Points.Count);
    }

    [Fact]
    public async Task ClientWatching_StopsTheLoopFromScreenshotting() {
        using var svc = NewService();
        var platform = new StubPlatform(Png());
        svc.SetClientWatching(Target.Id, true);

        Assert.True((await svc.AddAsync(platform, Target, new PixelWatchRequest(1, 1), CancellationToken.None)).Ok);
        int afterArm = platform.Screenshots;
        await Task.Delay(FastPoll * 5);

        Assert.Equal(1, afterArm);
        Assert.Equal(afterArm, platform.Screenshots);
    }

    [Fact]
    public async Task HitAsync_UnknownPoint_Fails() {
        using var svc = NewService();
        var platform = new StubPlatform(Png());
        svc.SetClientWatching(Target.Id, true);
        await svc.AddAsync(platform, Target, new PixelWatchRequest(3, 3), CancellationToken.None);

        var hit = await svc.HitAsync(platform, Target, "nope", CancellationToken.None);

        Assert.False(hit.Ok);
        Assert.Equal("unknown watch point", hit.Note);
    }

    [Fact]
    public async Task HitAsync_InsideTheRateWindow_DoesNotTapAgain() {
        var time = new ManualTime { Now = DateTimeOffset.UnixEpoch };
        using var svc = NewService(time);
        var platform = new StubPlatform(Png());
        svc.SetClientWatching(Target.Id, true);
        var added = await svc.AddAsync(platform, Target, new PixelWatchRequest(2, 2, RateMs: 500), CancellationToken.None);
        string id = added.Value!.Points[0].Id;

        await svc.HitAsync(platform, Target, id, CancellationToken.None);
        time.Now += TimeSpan.FromMilliseconds(100);
        var second = await svc.HitAsync(platform, Target, id, CancellationToken.None);
        time.Now += TimeSpan.FromMilliseconds(500);
        await svc.HitAsync(platform, Target, id, CancellationToken.None);

        Assert.Equal("cycling", second.Note);
        Assert.Equal(2, platform.Taps);
    }

    [Fact]
    public async Task AddGroup_RejectsMixedColours() {
        using var svc = NewService();
        var platform = new StubPlatform(Png(i => i[5, 5] = new Rgba32(200, 20, 20)));
        svc.SetClientWatching(Target.Id, true);
        var a = (await svc.AddAsync(platform, Target, new PixelWatchRequest(1, 1), CancellationToken.None)).Value!.Points[0].Id;
        var b = (await svc.AddAsync(platform, Target, new PixelWatchRequest(5, 5), CancellationToken.None)).Value!.Points[1].Id;

        var grouped = svc.AddGroup(Target.Id, new PixelWatchGroupRequest([a, b], 100));

        Assert.False(grouped.Ok);
        Assert.Equal("only points locked to the same colour can be grouped", grouped.Note);
    }

    [Fact]
    public async Task HitAsync_OnAGroupLead_TapsEveryMemberInOrder() {
        using var svc = NewService();
        var platform = new StubPlatform(Png());
        svc.SetClientWatching(Target.Id, true);
        var a = (await svc.AddAsync(platform, Target, new PixelWatchRequest(1, 1), CancellationToken.None)).Value!.Points[0].Id;
        var b = (await svc.AddAsync(platform, Target, new PixelWatchRequest(2, 2), CancellationToken.None)).Value!.Points[1].Id;
        Assert.True(svc.AddGroup(Target.Id, new PixelWatchGroupRequest([a, b], 0)).Ok);

        await svc.HitAsync(platform, Target, a, CancellationToken.None);

        Assert.Equal([(1, 1), (2, 2)], platform.Tapped);
    }

    [Fact]
    public async Task Remove_LastGroupMember_DissolvesTheGroup() {
        using var svc = NewService();
        var platform = new StubPlatform(Png());
        svc.SetClientWatching(Target.Id, true);
        var a = (await svc.AddAsync(platform, Target, new PixelWatchRequest(1, 1), CancellationToken.None)).Value!.Points[0].Id;
        var b = (await svc.AddAsync(platform, Target, new PixelWatchRequest(2, 2), CancellationToken.None)).Value!.Points[1].Id;
        svc.AddGroup(Target.Id, new PixelWatchGroupRequest([a, b], 0));

        Assert.True(svc.Remove(Target.Id, b));

        var state = svc.State(Target.Id);
        Assert.Empty(state.Groups);
        Assert.Null(state.Points[0].GroupId);
    }

    private static byte[] Png(Action<Image<Rgba32>> paint) {
        using var image = new Image<Rgba32>(64, 64, new Rgba32(10, 10, 10));
        paint(image);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private sealed class ManualTime : TimeProvider {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubPlatform(byte[] png) : DevicePlatformBase("android", [], [], [], [], []) {
        private const string Refusal = "stub platform";
        private int _screenshots;

        public int Screenshots => Volatile.Read(ref _screenshots);

        public List<(int X, int Y)> Tapped { get; } = [];

        public int Taps => Tapped.Count;

        public override Task<DeviceResult<byte[]>> ScreenshotAsync(DeviceTarget target, CancellationToken ct) {
            Interlocked.Increment(ref _screenshots);
            return Task.FromResult(DeviceResult<byte[]>.Success(png));
        }

        public override Task<DeviceResult> TapPointAsync(DeviceTarget target, int x, int y, CancellationToken ct) {
            lock (Tapped) Tapped.Add((x, y));
            return Task.FromResult(DeviceResult.Success());
        }

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
