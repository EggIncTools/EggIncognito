using EggIncognito.Services.Devices;
using SkiaSharp;

namespace EggIncognito.Tests.Devices;

public class DeviceFrameEncoderTests {
    [Fact]
    public void ToJpeg_ReencodesAPng() {
        var jpeg = DeviceFrameEncoder.ToJpeg(TestPng.Make(16), DeviceFrameEncoder.DefaultQuality);

        Assert.NotNull(jpeg);
        Assert.Equal([0xFF, 0xD8], jpeg[..2]);
        using var decoded = SKBitmap.Decode(jpeg);
        Assert.Equal(16, decoded.Width);
    }

    [Fact]
    public void ToJpeg_BrokenBytes_IsNull() => Assert.Null(DeviceFrameEncoder.ToJpeg([1, 2, 3], 75));
}
