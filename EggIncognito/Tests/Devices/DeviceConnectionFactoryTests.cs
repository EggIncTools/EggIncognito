using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices.Fake;

namespace EggIncognito.Tests.Devices;

public class DeviceConnectionFactoryTests {
    private static DeviceCaptureConfig CaptureConfig() => new() {
        IosSshHost = "phone.local",
        IosSshKeyPath = "/keys/phone"
    };

    private static DeviceTarget AndroidTarget => new("a1", Platforms.Android, "SER", "com.auxbrain.egginc");

    private static DeviceTarget IosTarget => new("i1", Platforms.Ios, "UDID", "com.auxbrain.egginc");

    [Fact]
    public void For_Android_ReturnsAdbConnection() {
        var factory = new DeviceConnectionFactory(new RefusingProcessRunner(), CaptureConfig());

        var conn = factory.For(AndroidTarget);

        Assert.IsType<AdbDeviceConnection>(conn);
    }

    [Fact]
    public void For_Ios_ReturnsSshConnection() {
        var factory = new DeviceConnectionFactory(new RefusingProcessRunner(), CaptureConfig());

        var conn = factory.For(IosTarget);

        Assert.IsType<SshDeviceConnection>(conn);
    }

    [Fact]
    public void For_UnknownPlatform_ReturnsNull() {
        var factory = new DeviceConnectionFactory(new RefusingProcessRunner(), CaptureConfig());

        Assert.Null(factory.For(new DeviceTarget("x1", "switch", "SER", "com.auxbrain.egginc")));
    }

    [Fact]
    public void Ios_HostAndKeyConfigured_ReturnsSshConnection() {
        var factory = new DeviceConnectionFactory(new RefusingProcessRunner(), CaptureConfig());

        Assert.NotNull(factory.Ios());
    }
}
