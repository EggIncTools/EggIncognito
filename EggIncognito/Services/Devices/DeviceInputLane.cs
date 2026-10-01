using System.Collections.Concurrent;

namespace EggIncognito.Services.Devices;

public static class DeviceInputLane {
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Lanes = new(StringComparer.Ordinal);

    public static async Task<T> RunAsync<T>(string deviceId, Func<CancellationToken, Task<T>> work,
        CancellationToken ct) {
        var lane = Lanes.GetOrAdd(deviceId, _ => new SemaphoreSlim(1, 1));
        await lane.WaitAsync(ct);
        try {
            return await work(ct);
        } finally {
            lane.Release();
        }
    }

    public static bool IsBusy(string deviceId) =>
        Lanes.TryGetValue(deviceId, out var lane) && lane.CurrentCount == 0;
}
