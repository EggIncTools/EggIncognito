using System.Collections.Concurrent;

namespace EggIncognito.Services.Devices;

public static class DeviceStreamGate {
    public static readonly TimeSpan HandoverWait = TimeSpan.FromSeconds(5);

    private static readonly ConcurrentDictionary<string, Slot> Slots = new(StringComparer.Ordinal);

    public static Task<DeviceStreamLease?> TryEnterAsync(string deviceId, CancellationToken ct) =>
        TryEnterAsync(deviceId, HandoverWait, ct);

    internal static async Task<DeviceStreamLease?> TryEnterAsync(string deviceId, TimeSpan wait,
        CancellationToken ct) {
        var slot = Slots.GetOrAdd(deviceId, _ => new Slot());
        slot.Preempt();
        if (!await slot.Gate.WaitAsync(wait, ct)) return null;
        return slot.Hand(ct);
    }

    public static bool IsHeld(string deviceId) =>
        Slots.TryGetValue(deviceId, out var slot) && slot.Gate.CurrentCount == 0;

    private sealed class Slot {
        private readonly Lock _lock = new();
        private DeviceStreamLease? _holder;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public void Preempt() {
            DeviceStreamLease? holder;
            lock (_lock) holder = _holder;
            holder?.Preempt();
        }

        public DeviceStreamLease Hand(CancellationToken ct) {
            var lease = new DeviceStreamLease(Release, ct);
            lock (_lock) _holder = lease;
            return lease;
        }

        private void Release(DeviceStreamLease lease) {
            lock (_lock) {
                if (ReferenceEquals(_holder, lease)) _holder = null;
            }

            Gate.Release();
        }
    }
}

public sealed class DeviceStreamLease : IDisposable {
    private readonly Action<DeviceStreamLease> _release;
    private readonly CancellationTokenSource _cts;
    private int _disposed;

    internal DeviceStreamLease(Action<DeviceStreamLease> release, CancellationToken ct) {
        _release = release;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    }

    public CancellationToken Token => _cts.Token;

    public bool Preempted { get; private set; }

    internal void Preempt() {
        Preempted = true;
        try {
            _cts.Cancel();
        } catch (ObjectDisposedException) {
        }
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _release(this);
        _cts.Dispose();
    }
}
