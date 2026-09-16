using System.Collections.Concurrent;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;
using SixLabors.ImageSharp.PixelFormats;

namespace EggIncognito.Services.Devices;

public sealed class PixelWatchService(ILogger<PixelWatchService> logger) : IDisposable {
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(350);
    public static readonly TimeSpan AfterTap = TimeSpan.FromMilliseconds(700);
    public const int Tolerance = 48;
    public const int MaxPoints = 8;

    private readonly ConcurrentDictionary<string, Watch> _watches = new(StringComparer.Ordinal);

    public event Action<string, PixelWatchState>? Changed;

    public PixelWatchState State(string deviceId) =>
        _watches.TryGetValue(deviceId, out var w) ? w.State : PixelWatchState.Empty;

    public async Task<DeviceResult<PixelWatchState>> AddAsync(
        IDevicePlatform platform, DeviceTarget target, int x, int y, CancellationToken ct) {
        var shot = await platform.ScreenshotAsync(target, ct);
        if (!shot.Ok || shot.Value is not { Length: > 0 } png) {
            string note = shot.Note ?? "screenshot failed";
            return shot.Outcome == DeviceOutcome.Unreachable
                ? DeviceResult<PixelWatchState>.Unreachable(note)
                : DeviceResult<PixelWatchState>.Error(note);
        }

        if (PixelSampler.Sample(png, x, y) is not { } color)
            return DeviceResult<PixelWatchState>.Error("that point is outside the device screenshot");

        var watch = _watches.GetOrAdd(target.Id, _ => new Watch(target));
        lock (watch.Gate) {
            if (watch.Points.Count >= MaxPoints)
                return DeviceResult<PixelWatchState>.Error($"at most {MaxPoints} watch points per device");
            watch.Points.Add(new Point(x, y, color));
            watch.Loop ??= Task.Run(() => LoopAsync(platform, watch), CancellationToken.None);
        }

        var state = watch.State;
        Publish(target.Id, state);
        return DeviceResult<PixelWatchState>.Success(state);
    }

    public bool Remove(string deviceId, string pointId) {
        if (!_watches.TryGetValue(deviceId, out var watch)) return false;
        bool empty;
        bool removed;
        lock (watch.Gate) {
            removed = watch.Points.RemoveAll(p => p.Id == pointId) > 0;
            empty = watch.Points.Count == 0;
        }

        if (!removed) return false;
        if (empty) return StopAll(deviceId);
        Publish(deviceId, watch.State);
        return true;
    }

    public bool StopAll(string deviceId) {
        if (!_watches.TryRemove(deviceId, out var watch)) return false;
        watch.Cts.Cancel();
        Publish(deviceId, PixelWatchState.Empty);
        return true;
    }

    private async Task LoopAsync(IDevicePlatform platform, Watch w) {
        var ct = w.Cts.Token;
        try {
            foreach (var p in w.Snapshot()) await TapAsync(platform, w, p, ct);
            while (!ct.IsCancellationRequested) {
                await Task.Delay(Poll, ct);
                var points = w.Snapshot();
                if (points.Count == 0) continue;

                var shot = await platform.ScreenshotAsync(w.Target, ct);
                if (!shot.Ok || shot.Value is not { Length: > 0 } png) {
                    foreach (var p in points) p.Error = shot.Note ?? "screenshot failed";
                    Publish(w.Target.Id, w.State);
                    continue;
                }

                bool tapped = false;
                foreach (var p in points) {
                    if (ct.IsCancellationRequested) break;
                    if (PixelSampler.Sample(png, p.X, p.Y) is not { } px) continue;
                    if (!PixelSampler.Close(px, p.Color, Tolerance)) continue;
                    await TapAsync(platform, w, p, ct);
                    tapped = true;
                }

                if (tapped) await Task.Delay(AfterTap, ct);
            }
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "pixel watch for {Device} cancelled", w.Target.Id);
        } catch (Exception ex) {
            logger.LogWarning(ex, "pixel watch for {Device} stopped", w.Target.Id);
            foreach (var p in w.Snapshot()) p.Error = ex.Message;
            Publish(w.Target.Id, w.State);
        }
    }

    private async Task TapAsync(IDevicePlatform platform, Watch w, Point p, CancellationToken ct) {
        Interlocked.Increment(ref w.TapDepth);
        Publish(w.Target.Id, w.State);
        try {
            var tap = await platform.TapPointAsync(w.Target, p.X, p.Y, ct);
            if (tap.Ok) {
                p.Taps++;
                p.LastTapAt = DateTimeOffset.UtcNow;
                p.Error = null;
            } else {
                p.Error = tap.Note ?? "tap failed";
            }
        } finally {
            Interlocked.Decrement(ref w.TapDepth);
        }

        Publish(w.Target.Id, w.State);
    }

    private void Publish(string deviceId, PixelWatchState state) {
        try {
            Changed?.Invoke(deviceId, state);
        } catch (Exception ex) {
            logger.LogDebug(ex, "pixel watch listener threw");
        }
    }

    public void Dispose() {
        foreach (var id in _watches.Keys.ToArray()) StopAll(id);
    }

    private sealed class Watch(DeviceTarget target) {
        public DeviceTarget Target { get; } = target;
        public CancellationTokenSource Cts { get; } = new();
        public Task? Loop { get; set; }
        public List<Point> Points { get; } = [];
        public object Gate { get; } = new();
        public int TapDepth;

        public List<Point> Snapshot() {
            lock (Gate) return [.. Points];
        }

        public PixelWatchState State =>
            new([.. Snapshot().Select(p => p.Status)], Volatile.Read(ref TapDepth) > 0);
    }

    private sealed class Point(int x, int y, Rgba32 color) {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
        public int X { get; } = x;
        public int Y { get; } = y;
        public Rgba32 Color { get; } = color;
        public int Taps { get; set; }
        public DateTimeOffset? LastTapAt { get; set; }
        public string? Error { get; set; }

        public PixelWatchStatus Status => new(Id, X, Y, PixelSampler.Hex(Color), Taps, LastTapAt, Error);
    }
}
