using EggIncognito.Capture;

namespace EggIncognito.Services.Devices;

public interface IDeviceResponseSources {
    ICaptureResponseSource? For(string deviceId);
}
