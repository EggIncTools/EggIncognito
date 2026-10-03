namespace EggIncognito.Services.Devices;

public sealed class DeviceActivity(
    DeviceClaimRegistry claims,
    PixelWatchService? watches = null,
    CookbookCancellations? cookbooks = null) {
    public bool IsBusy(string deviceId) => Why(deviceId) is not null;

    public string? Why(string deviceId) {
        if (string.IsNullOrEmpty(deviceId)) return null;
        if (DeviceStreamGate.IsHeld(deviceId)) return "screen stream open";
        if (claims.IsHeld(deviceId)) return "claimed";
        if (cookbooks?.IsRunning(deviceId) == true) return "cookbook running";
        if (watches?.State(deviceId) is { Points.Count: > 0, Paused: false }) return "pixel watch armed";
        return null;
    }
}
