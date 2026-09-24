namespace EggIncognito.Core.Services.Devices;

public enum StoreAvailability { Unknown, UpToDate, UpdateOffered, ManualNeeded }

public sealed record StoreProbeOutcome(StoreAvailability Availability, string? StoreVersion, string? Note);

public sealed record TriggerOutcome(bool Ok, string? Note);

public interface IStoreUpdateDriver {
    string Platform { get; }
    string StoreName { get; }
    Task<string?> ReadInstalledAsync(DeviceTarget target, CancellationToken ct);
    Task PrepareAsync(DeviceTarget target, CancellationToken ct);
    Task<StoreProbeOutcome> ProbeStoreAsync(DeviceTarget target, string installed, Func<string, Task>? progress, CancellationToken ct);
    Task<TriggerOutcome> TriggerInstallAsync(DeviceTarget target, Func<string, Task>? progress, CancellationToken ct);
    Task<bool> ProbeInstallCompleteAsync(DeviceTarget target, CancellationToken ct) => Task.FromResult(false);
    Task CleanupAsync(DeviceTarget target, CancellationToken ct);
}
