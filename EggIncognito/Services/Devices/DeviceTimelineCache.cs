using System.Collections.Concurrent;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices;

public sealed class DeviceTimelineCache(IServiceScopeFactory scopes, TimeProvider time) : IDeviceJobSink, IDisposable {
    private const int Keep = 50;

    private sealed class Entry {
        public List<DeviceJobRow> Jobs = [];
        public ConcurrentDictionary<long, IReadOnlyList<DeviceJobLineRow>> Lines = new();
        public bool Loaded;
    }

    private readonly ConcurrentDictionary<string, Entry> _byDevice = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, (long Mark, int Unfinished)> _observed =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _gate = new(1, 1);

    public static bool NeedsRefill(long cached, long observed) => cached != observed;

    public void Touched(string deviceId) {
        if (_byDevice.TryGetValue(deviceId, out var e)) e.Loaded = false;
    }

    public void Invalidate() {
        foreach (var e in _byDevice.Values) e.Loaded = false;
    }

    public async Task<IReadOnlyList<DeviceJobRow>> HistoryAsync(string deviceId, int n, string? kind,
        CancellationToken ct) {
        var e = await LoadAsync(deviceId, ct);
        var rows = string.IsNullOrEmpty(kind) ? e.Jobs : [.. e.Jobs.Where(j => j.Kind == kind)];
        return [.. rows.Take(n)];
    }

    public async Task<IReadOnlyList<DeviceJobRow>> PageAsync(string deviceId, int n, long? before,
        CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<DeviceJobStore>();
        return await store.PageAsync(deviceId, n, before, null, ct);
    }

    public async Task<IReadOnlyList<DeviceJobLineRow>> LinesAsync(string deviceId, long jobId,
        CancellationToken ct) {
        var e = await LoadAsync(deviceId, ct);
        if (e.Lines.TryGetValue(jobId, out var cached)) return cached;
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<DeviceJobStore>();
        var lines = await store.LinesAsync(jobId, ct);
        e.Lines[jobId] = lines;
        return lines;
    }

    public async Task<DeviceJobRow?> LatestAsync(string deviceId, string kind, CancellationToken ct) {
        var e = await LoadAsync(deviceId, ct);
        return e.Jobs.Find(j => j.Kind == kind);
    }

    public async Task<IReadOnlyList<DeviceJobRow>> LatestPerDeviceAsync(IReadOnlyList<string> deviceIds,
        string kind, CancellationToken ct) {
        var outRows = new List<DeviceJobRow>();
        foreach (string id in deviceIds) {
            if (await LatestAsync(id, kind, ct) is { } row) outRows.Add(row);
        }

        return outRows;
    }

    public async Task<IReadOnlyList<DeviceJobRow>> RunningAsync(IReadOnlyList<string> deviceIds,
        CancellationToken ct) {
        var outRows = new List<DeviceJobRow>();
        foreach (string id in deviceIds) {
            var e = await LoadAsync(id, ct);
            outRows.AddRange(e.Jobs.Where(j => j.State == DeviceJobStates.Running));
        }

        return outRows;
    }

    public async Task<IReadOnlyList<DeviceProbeStats>> StatsAsync(IReadOnlyList<string> deviceIds, TimeSpan window,
        CancellationToken ct) {
        var now = time.GetUtcNow();
        var outRows = new List<DeviceProbeStats>();
        foreach (string id in deviceIds) {
            var e = await LoadAsync(id, ct);
            var probes = e.Jobs.Where(j => j.Kind == DeviceJobKinds.Probe)
                .Select(j => (j.StartedAt, j.Reachable, j.Outcome)).ToList();
            outRows.Add(DeviceJobStore.StatsFor(id, probes, now - window));
        }

        return outRows;
    }

    public async Task<bool> RefreshMovedAsync(CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<DeviceJobStore>();
        var marks = await store.WatermarksAsync(ct);
        var moved = false;
        foreach (var (deviceId, mark, unfinished) in marks) {
            bool first = !_observed.TryGetValue(deviceId, out var prev);
            _observed[deviceId] = (mark, unfinished);
            if (first || (!NeedsRefill(prev.Mark, mark) && prev.Unfinished == unfinished)) continue;
            if (_byDevice.TryGetValue(deviceId, out var e)) e.Loaded = false;
            moved = true;
        }

        return moved;
    }

    private async Task<Entry> LoadAsync(string deviceId, CancellationToken ct) {
        var e = _byDevice.GetOrAdd(deviceId, _ => new Entry());
        if (e.Loaded) return e;
        await _gate.WaitAsync(ct);
        try {
            if (e.Loaded) return e;
            using var scope = scopes.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<DeviceJobStore>();
            var jobs = await store.HistoryAsync(deviceId, Keep, null, ct);
            e.Jobs = [.. jobs];
            e.Lines = new();
            e.Loaded = true;
            return e;
        } finally {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
