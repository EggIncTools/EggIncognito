namespace EggIncognito.Core.Services.Devices;

public interface IDeviceStoreChecker {
    string Platform { get; }

    Task<StoreCheckResult> CheckAndUpdateAsync(
        DeviceTarget device, CancellationToken ct, Func<string, Task>? progress = null);
}

public sealed record StoreCheckResult(
    bool Reachable,
    string? InstalledBefore,
    string? InstalledAfter,
    bool UpdateFound,
    bool Installed,
    string Action,
    string? Note);
