using System.Collections.Concurrent;

namespace EggIncognito.Services.Devices;

public static class DeviceStreamGate {
    public static readonly TimeSpan HandoverWait = TimeSpan.FromSeconds(3);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    public static Task<bool> TryEnterAsync(string deviceId, CancellationToken ct) =>
        Gate(deviceId).WaitAsync(HandoverWait, ct);

    public static void Exit(string deviceId) {
        if (Gates.TryGetValue(deviceId, out var gate)) gate.Release();
    }

    private static SemaphoreSlim Gate(string deviceId) => Gates.GetOrAdd(deviceId, _ => new SemaphoreSlim(1, 1));
}
