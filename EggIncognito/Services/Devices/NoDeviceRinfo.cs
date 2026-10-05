using EggIncognito.Capture;

namespace EggIncognito.Services.Devices;

internal sealed class NoDeviceRinfo : IDeviceRinfo {
    public static readonly NoDeviceRinfo Instance = new();

    public DeviceRinfo? Latest(string deviceId) => null;
}
