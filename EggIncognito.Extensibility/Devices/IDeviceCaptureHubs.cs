using EggIncognito.Capture;

namespace EggIncognito.Services.Devices;

public interface IDeviceCaptureHubs {
    CaptureHub? HubFor(string deviceId);
}
