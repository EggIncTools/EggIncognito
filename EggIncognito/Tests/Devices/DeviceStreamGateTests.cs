using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DeviceStreamGateTests {
    [Fact]
    public async Task SecondEntry_PreemptsTheFirstHolder() {
        string id = $"gate-{Guid.NewGuid():N}";
        var first = await DeviceStreamGate.TryEnterAsync(id, CancellationToken.None);
        Assert.NotNull(first);

        var second = DeviceStreamGate.TryEnterAsync(id, CancellationToken.None);
        Assert.True(first.Token.IsCancellationRequested);
        Assert.True(first.Preempted);
        Assert.False(second.IsCompleted);

        first.Dispose();
        using var next = await second;
        Assert.NotNull(next);
        Assert.False(next.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task HolderThatNeverLetsGo_TimesOutTheNewcomer() {
        string id = $"gate-{Guid.NewGuid():N}";
        using var stuck = await DeviceStreamGate.TryEnterAsync(id, CancellationToken.None);
        Assert.NotNull(stuck);

        Assert.Null(await DeviceStreamGate.TryEnterAsync(id, TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [Fact]
    public async Task Dispose_ReleasesTheKeyOnce() {
        string id = $"gate-{Guid.NewGuid():N}";
        var lease = await DeviceStreamGate.TryEnterAsync(id, CancellationToken.None);
        Assert.NotNull(lease);
        lease.Dispose();
        lease.Dispose();

        using var a = await DeviceStreamGate.TryEnterAsync(id, CancellationToken.None);
        Assert.NotNull(a);
        Assert.True(DeviceStreamGate.IsHeld(id));
    }

    [Fact]
    public async Task IsHeld_TracksTheHolder() {
        string id = $"gate-{Guid.NewGuid():N}";
        Assert.False(DeviceStreamGate.IsHeld(id));
        var lease = await DeviceStreamGate.TryEnterAsync(id, CancellationToken.None);
        Assert.NotNull(lease);
        Assert.True(DeviceStreamGate.IsHeld(id));
        lease.Dispose();
        Assert.False(DeviceStreamGate.IsHeld(id));
    }

    [Fact]
    public async Task Keys_AreIndependent() {
        using var a = await DeviceStreamGate.TryEnterAsync($"gate-{Guid.NewGuid():N}", CancellationToken.None);
        using var b = await DeviceStreamGate.TryEnterAsync($"gate-{Guid.NewGuid():N}", CancellationToken.None);
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.False(a.Token.IsCancellationRequested);
    }
}
