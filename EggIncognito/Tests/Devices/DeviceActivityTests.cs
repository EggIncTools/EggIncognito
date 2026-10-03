using EggIncognito.Services.Devices;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Devices;

public class DeviceActivityTests {
    [Fact]
    public void Idle_WhenNothingHoldsTheDevice() {
        var activity = new DeviceActivity(new DeviceClaimRegistry(TimeProvider.System), new PixelWatchService(NullLogger<PixelWatchService>.Instance, TimeProvider.System), new CookbookCancellations());

        Assert.False(activity.IsBusy("d1"));
        Assert.Null(activity.Why("d1"));
    }

    [Fact]
    public void Busy_WhileACookbookJobIsLive() {
        var cookbooks = new CookbookCancellations();
        var activity = new DeviceActivity(new DeviceClaimRegistry(TimeProvider.System), null, cookbooks);
        using var cts = new CancellationTokenSource();

        cookbooks.Register("d1", 7, cts);
        Assert.Equal("cookbook running", activity.Why("d1"));

        cookbooks.Release("d1", 7, cts);
        Assert.False(activity.IsBusy("d1"));
    }
}
