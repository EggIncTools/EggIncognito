namespace EggIncognito.Services.Devices;

public sealed class DeviceActivity(DeviceClaimRegistry claims) {
    public bool IsBusy(string deviceId) {
        if (string.IsNullOrEmpty(deviceId)) return false;
        return DeviceStreamGate.IsHeld(deviceId) || claims.IsHeld(deviceId);
    }
}
