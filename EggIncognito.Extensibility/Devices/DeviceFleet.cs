namespace EggIncognito.Services.Devices;

public interface IDeviceFleet {
    Task<IReadOnlyList<DeviceEntry>> EnabledAsync(CancellationToken ct);
    Task PersistCapturePortAsync(string deviceId, int port, CancellationToken ct);
}

public sealed record DeviceEntry(
    string Id, string Platform, string Label, string Target, string Package,
    string Origin = DeviceEntry.RuntimeOrigin, int? CapturePort = null) {
    public const string RuntimeOrigin = "runtime";
}
