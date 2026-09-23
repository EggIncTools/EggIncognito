using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DeviceTimelineCacheTests {
    [Fact]
    public void UnmovedWatermarkDoesNotRefill() => Assert.False(DeviceTimelineCache.NeedsRefill(41, 41));

    [Fact]
    public void ForwardMoveRefills() => Assert.True(DeviceTimelineCache.NeedsRefill(41, 42));

    [Fact]
    public void BackwardMoveRefills() => Assert.True(DeviceTimelineCache.NeedsRefill(42, 41));
}
