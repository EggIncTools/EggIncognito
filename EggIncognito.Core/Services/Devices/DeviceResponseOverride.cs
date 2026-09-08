namespace EggIncognito.Core.Services.Devices;

public sealed record DeviceResponseOverride(string Path, string BodyBase64, string ContentType = "text/html");

public sealed record DeviceResponseOverrideSet(IReadOnlyList<DeviceResponseOverride> Entries);

public interface IDeviceResponseOverrides {
    Task<DeviceResult> SetAsync(string deviceId, IReadOnlyList<DeviceResponseOverride> entries, CancellationToken ct);
    Task<DeviceResult> ClearAsync(string deviceId, CancellationToken ct);
}
