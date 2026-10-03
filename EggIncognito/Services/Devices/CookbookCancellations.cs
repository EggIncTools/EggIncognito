using System.Collections.Concurrent;

namespace EggIncognito.Services.Devices;

public sealed class CookbookCancellations {
    private readonly ConcurrentDictionary<string, (long JobId, CancellationTokenSource Cts)> _live =
        new(StringComparer.Ordinal);

    public void Register(string deviceId, long jobId, CancellationTokenSource cts) => _live[deviceId] = (jobId, cts);

    public void Release(string deviceId, long jobId, CancellationTokenSource cts) =>
        _live.TryRemove(new KeyValuePair<string, (long, CancellationTokenSource)>(deviceId, (jobId, cts)));

    public bool IsRunning(string deviceId) => _live.ContainsKey(deviceId);

    public bool TryCancel(string deviceId, out long jobId) {
        jobId = 0;
        if (!_live.TryGetValue(deviceId, out var live)) return false;
        jobId = live.JobId;
        live.Cts.Cancel();
        return true;
    }
}
