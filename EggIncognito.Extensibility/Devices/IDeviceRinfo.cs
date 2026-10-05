using EggIncognito.Capture;

namespace EggIncognito.Services.Devices;

public interface IDeviceRinfo {
    DeviceRinfo? Latest(string deviceId);
}
