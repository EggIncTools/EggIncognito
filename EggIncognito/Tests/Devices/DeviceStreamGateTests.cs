using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DeviceStreamGateTests {
    [Fact]
    public async Task SecondEntry_WaitsForTheFirstHolderToExit() {
        string id = $"gate-{Guid.NewGuid():N}";
        Assert.True(await DeviceStreamGate.TryEnterAsync(id, CancellationToken.None));

        var second = DeviceStreamGate.TryEnterAsync(id, CancellationToken.None);
        await Task.Delay(100);
        Assert.False(second.IsCompleted);

        DeviceStreamGate.Exit(id);
        Assert.True(await second);
        DeviceStreamGate.Exit(id);
    }

    [Fact]
    public async Task Exit_ReleasesTheKeyForTheNextHolder() {
        string id = $"gate-{Guid.NewGuid():N}";
        Assert.True(await DeviceStreamGate.TryEnterAsync(id, CancellationToken.None));
        DeviceStreamGate.Exit(id);
        Assert.True(await DeviceStreamGate.TryEnterAsync(id, CancellationToken.None));
        DeviceStreamGate.Exit(id);
    }

    [Fact]
    public async Task IsHeld_TracksTheHolder() {
        string id = $"gate-{Guid.NewGuid():N}";
        Assert.False(DeviceStreamGate.IsHeld(id));
        Assert.True(await DeviceStreamGate.TryEnterAsync(id, CancellationToken.None));
        Assert.True(DeviceStreamGate.IsHeld(id));
        DeviceStreamGate.Exit(id);
        Assert.False(DeviceStreamGate.IsHeld(id));
    }

    [Fact]
    public async Task Keys_AreIndependent() {
        string a = $"gate-{Guid.NewGuid():N}";
        string b = $"gate-{Guid.NewGuid():N}";
        Assert.True(await DeviceStreamGate.TryEnterAsync(a, CancellationToken.None));
        Assert.True(await DeviceStreamGate.TryEnterAsync(b, CancellationToken.None));
        DeviceStreamGate.Exit(a);
        DeviceStreamGate.Exit(b);
    }
}
