using EggIncognito.Capture;

namespace EggIncognito.Services.Devices;

public interface IDeviceResponseTransforms {
    ICaptureResponseTransform? For(string deviceId);
}
