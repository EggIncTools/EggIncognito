using EggIncognito.Capture;

namespace EggIncognito.Tests;

public sealed class CaptureSessionTests : IDisposable {
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private CaptureSession NewSession(out FakeCaptureProxy fake) {
        var f = new FakeCaptureProxy();
        fake = f;
        string contentRoot = TestPaths.WebProjectDir();
        string tmp = _tmp.CreateSubdir();
        var opts = new CaptureSessionOptions(18080, null, null,
            false, false, tmp, Path.Combine(tmp, "ca.cer"));
        return new CaptureSession(contentRoot, opts, _ => f);
    }

    [Fact]
    public async Task Start_SetsRunning_AndStartsProxyOnce() {
        var s = NewSession(out var fake);
        var result = await s.StartAsync(CancellationToken.None);
        Assert.True(s.Status.Running);
        Assert.Equal(1, fake.StartCount);
        Assert.Equal(18080, result.Port);
        await s.StopAsync();
    }

    [Fact]
    public async Task Start_IsIdempotent() {
        var s = NewSession(out var fake);
        await s.StartAsync(CancellationToken.None);
        await s.StartAsync(CancellationToken.None);
        Assert.Equal(1, fake.StartCount);
        await s.StopAsync();
    }

    [Fact]
    public async Task Stop_IsIdempotent_AndStopsProxy() {
        var s = NewSession(out var fake);
        await s.StartAsync(CancellationToken.None);
        await s.StopAsync();
        await s.StopAsync();
        Assert.Equal(1, fake.StopCount);
        Assert.False(s.Status.Running);
    }

    [Fact]
    public async Task StartStopStart_RoundTrips() {
        var s = NewSession(out var fake);
        await s.StartAsync(CancellationToken.None);
        await s.StopAsync();
        await s.StartAsync(CancellationToken.None);
        Assert.True(s.Status.Running);
        Assert.Equal(2, fake.StartCount);
        await s.StopAsync();
    }

    [Fact]
    public async Task Start_ProxyStartThrows_ResetsToStopped_AndRemainsRestartable() {
        var s = NewSession(out var fake);
        fake.ThrowOnStart = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.StartAsync(CancellationToken.None));
        Assert.Equal(CaptureState.Stopped, s.State);
        Assert.Null(s.Hub.DevicesChanged);
        Assert.Equal(1, fake.DisposeCount);

        fake.ThrowOnStart = false;
        await s.StartAsync(CancellationToken.None);
        Assert.True(s.Status.Running);
        Assert.Equal(2, fake.StartCount);
        await s.StopAsync();
    }

    [Fact]
    public async Task Stop_ProxyStopThrows_StillResetsToStopped() {
        var s = NewSession(out var fake);
        await s.StartAsync(CancellationToken.None);
        fake.ThrowOnStop = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.StopAsync());
        Assert.Equal(CaptureState.Stopped, s.State);
        Assert.False(s.Status.Running);
        Assert.Equal(1, fake.DisposeCount);

        fake.ThrowOnStop = false;
        await s.StartAsync(CancellationToken.None);
        Assert.True(s.Status.Running);
        await s.StopAsync();
    }

    [Fact]
    public async Task Stop_ClearsDevicesChangedSubscription() {
        var s = NewSession(out _);
        await s.StartAsync(CancellationToken.None);
        Assert.NotNull(s.Hub.DevicesChanged);
        await s.StopAsync();
        Assert.Null(s.Hub.DevicesChanged);
    }

    [Fact]
    public async Task Status_ReflectsActiveClients_AndResetsOnStop() {
        var s = NewSession(out var fake);
        await s.StartAsync(CancellationToken.None);
        fake.EmitConnect(3, "192.168.1.50");
        Assert.Equal(3, s.Status.ActiveClients);
        await s.StopAsync();
        Assert.Equal(0, s.Status.ActiveClients);
    }
}
