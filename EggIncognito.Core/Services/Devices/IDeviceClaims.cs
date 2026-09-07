namespace EggIncognito.Core.Services.Devices;

public interface IDeviceClaims {
    bool Active { get; }
    Task<DeviceResult<DateTimeOffset>> ClaimAsync(string deviceId, TimeSpan? ttl, CancellationToken ct);
    Task ReleaseAsync(string deviceId, CancellationToken ct);
}

public sealed class NullDeviceClaims(TimeProvider time) : IDeviceClaims {
    public bool Active => false;

    public Task<DeviceResult<DateTimeOffset>> ClaimAsync(string deviceId, TimeSpan? ttl, CancellationToken ct) =>
        Task.FromResult(DeviceResult<DateTimeOffset>.Success(time.GetUtcNow() + (ttl ?? TimeSpan.Zero)));

    public Task ReleaseAsync(string deviceId, CancellationToken ct) => Task.CompletedTask;
}
