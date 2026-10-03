using EggIncognito.Capture;
using EggIncognito.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public sealed class CaptureSweeperTests : IDisposable {
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private (CaptureSweeper Sweeper, CaptureSessionManager Manager) New() {
        var opts = HostedCaptureOptions.Defaults();
        var manager = new CaptureSessionManager(opts,
            (key, basePort, _) => CaptureSessionManagerTests.NewSession(_tmp,
                key == CaptureSessionManager.LocalKey ? 18080 : basePort));
        var sweeper = new CaptureSweeper(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            manager, opts, TimeProvider.System, NullLogger<CaptureSweeper>.Instance);
        return (sweeper, manager);
    }

    private static async Task<CaptureSession> StartedSession(
        CaptureSessionManager manager, string key, TimeSpan age, TimeSpan sinceLastFlow) {
        var s = manager.GetOrCreate(key);
        await s.StartAsync(CancellationToken.None);
        s.StartedUtc = Now - age;
        s.LastFlowUtc = Now - sinceLastFlow;
        return s;
    }

    [Fact]
    public async Task IdleSession_IsStoppedAndRemoved() {
        var (sweeper, manager) = New();
        var s = await StartedSession(manager, "user-a", TimeSpan.FromHours(1), TimeSpan.FromMinutes(31));
        await sweeper.SweepOnceAsync(Now);
        Assert.Equal(CaptureState.Stopped, s.State);
        Assert.Null(manager.Get("user-a"));
    }

    [Fact]
    public async Task CappedSession_IsStopped_EvenWhenActive() {
        var (sweeper, manager) = New();
        var s = await StartedSession(manager, "user-a", TimeSpan.FromHours(5), TimeSpan.FromMinutes(1));
        await sweeper.SweepOnceAsync(Now);
        Assert.Equal(CaptureState.Stopped, s.State);
        Assert.Null(manager.Get("user-a"));
    }

    [Fact]
    public async Task FreshSession_IsLeftAlone() {
        var (sweeper, manager) = New();
        var s = await StartedSession(manager, "user-a", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5));
        await sweeper.SweepOnceAsync(Now);
        Assert.Equal(CaptureState.Running, s.State);
        Assert.NotNull(manager.Get("user-a"));
        await s.StopAsync();
    }

    [Fact]
    public async Task LocalSession_IsNeverSwept() {
        var (sweeper, manager) = New();
        var s = await StartedSession(manager, CaptureSessionManager.LocalKey,
            TimeSpan.FromDays(2), TimeSpan.FromDays(1));
        await sweeper.SweepOnceAsync(Now);
        Assert.Equal(CaptureState.Running, s.State);
        Assert.NotNull(manager.Get(CaptureSessionManager.LocalKey));
        await s.StopAsync();
    }

    [Fact]
    public async Task StoppedSession_IsReleasedOnceIdle() {
        var (sweeper, manager) = New();
        var s = await StartedSession(manager, "user-a", TimeSpan.FromHours(1), TimeSpan.FromMinutes(31));
        await s.StopAsync();
        await sweeper.SweepOnceAsync(Now);
        Assert.Null(manager.Get("user-a"));
    }
}
