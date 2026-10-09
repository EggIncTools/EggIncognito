using System.Collections.Concurrent;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;
using SkiaSharp;

namespace EggIncognito.Services.Devices;

public sealed class PixelWatchService(ILogger<PixelWatchService> logger, TimeProvider time) : IDisposable {
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(500);
    public const int Tolerance = 48;
    public const int MaxPoints = 32;
    public const int DefaultRateMs = 300;
    public const int DefaultHoldMs = 800;
    public const int MinRateMs = 50;
    public const int MaxRateMs = 10_000;
    public const int MinHoldMs = 100;
    public const int MaxHoldMs = 10_000;
    public const int MaxOffsetMs = 5_000;
    public const int DoubleGapMs = 80;

    private readonly ConcurrentDictionary<string, Watch> _watches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _clientWatching = new(StringComparer.Ordinal);

    internal TimeSpan PollEvery { get; init; } = Poll;

    public event Action<string, PixelWatchState>? Changed;

    public PixelWatchState State(string deviceId) =>
        _watches.TryGetValue(deviceId, out var w) ? w.State : PixelWatchState.Empty;

    public bool ClientWatching(string deviceId) => _clientWatching.ContainsKey(deviceId);

    public void SetClientWatching(string deviceId, bool on) {
        if (on) _clientWatching[deviceId] = true;
        else _clientWatching.TryRemove(deviceId, out _);
    }

    public static int ClampRate(int? ms) => Math.Clamp(ms ?? DefaultRateMs, MinRateMs, MaxRateMs);

    public static int ClampHold(int? ms) => Math.Clamp(ms ?? DefaultHoldMs, MinHoldMs, MaxHoldMs);

    public static int ClampOffset(int ms) => Math.Clamp(ms, 0, MaxOffsetMs);

    public async Task<DeviceResult<PixelWatchState>> AddAsync(
        IDevicePlatform platform, DeviceTarget target, PixelWatchRequest req, CancellationToken ct) {
        var shot = await platform.ScreenshotAsync(target, ct);
        if (!shot.Ok || shot.Value is not { Length: > 0 } png) {
            string note = shot.Note ?? "screenshot failed";
            return shot.Outcome == DeviceOutcome.Unreachable
                ? DeviceResult<PixelWatchState>.Unreachable(note)
                : DeviceResult<PixelWatchState>.Error(note);
        }

        if (PixelSampler.Sample(png, req.X, req.Y) is not { } color)
            return DeviceResult<PixelWatchState>.Error("that point is outside the device screenshot");

        var watch = _watches.GetOrAdd(target.Id, _ => new Watch(target));
        lock (watch.Gate) {
            if (watch.Points.Count >= MaxPoints)
                return DeviceResult<PixelWatchState>.Error($"at most {MaxPoints} watch points per device");
            watch.Points.Add(new Point(req.X, req.Y, color) {
                Kind = PixelWatchKinds.Normalize(req.Kind),
                HoldMs = ClampHold(req.HoldMs),
                RateMs = ClampRate(req.RateMs)
            });
            watch.Loop ??= Task.Run(() => LoopAsync(platform, watch), CancellationToken.None);
        }

        var state = watch.State;
        Publish(target.Id, state);
        return DeviceResult<PixelWatchState>.Success(state);
    }

    public bool Update(string deviceId, string pointId, PixelWatchPointUpdate update) {
        if (!_watches.TryGetValue(deviceId, out var watch)) return false;
        lock (watch.Gate) {
            if (watch.Points.Find(p => p.Id == pointId) is not { } point) return false;
            if (update.Kind is { Length: > 0 } kind) point.Kind = PixelWatchKinds.Normalize(kind);
            if (update.HoldMs is { } hold) point.HoldMs = ClampHold(hold);
            if (update.RateMs is { } rate) point.RateMs = ClampRate(rate);
        }

        Publish(deviceId, watch.State);
        return true;
    }

    public DeviceResult<PixelWatchState> AddGroup(string deviceId, PixelWatchGroupRequest req) {
        if (!_watches.TryGetValue(deviceId, out var watch))
            return DeviceResult<PixelWatchState>.Error("no watch points are armed for this device");
        if (req.PointIds is not { Count: >= 2 })
            return DeviceResult<PixelWatchState>.Error("a group needs at least two points");

        lock (watch.Gate) {
            var members = new List<Point>(req.PointIds.Count);
            foreach (string id in req.PointIds) {
                if (watch.Points.Find(p => p.Id == id) is not { } point)
                    return DeviceResult<PixelWatchState>.Error("unknown watch point");
                if (point.GroupId is not null)
                    return DeviceResult<PixelWatchState>.Error("a point can belong to one group only");
                members.Add(point);
            }

            string color = PixelSampler.Hex(members[0].Color);
            if (members.Any(m => PixelSampler.Hex(m.Color) != color))
                return DeviceResult<PixelWatchState>.Error("only points locked to the same colour can be grouped");

            var group = new Group(color, ClampOffset(req.OffsetMs), [.. members.Select(m => m.Id)]);
            foreach (var m in members) m.GroupId = group.Id;
            watch.Groups.Add(group);
        }

        var state = watch.State;
        Publish(deviceId, state);
        return DeviceResult<PixelWatchState>.Success(state);
    }

    public bool Ungroup(string deviceId, string groupId) {
        if (!_watches.TryGetValue(deviceId, out var watch)) return false;
        lock (watch.Gate) {
            if (watch.Groups.RemoveAll(g => g.Id == groupId) == 0) return false;
            foreach (var p in watch.Points.Where(p => p.GroupId == groupId)) p.GroupId = null;
        }

        Publish(deviceId, watch.State);
        return true;
    }

    public bool SetPaused(string deviceId, bool paused) {
        if (!_watches.TryGetValue(deviceId, out var watch)) return false;
        watch.Paused = paused;
        Publish(deviceId, watch.State);
        return true;
    }

    public async Task<DeviceResult<PixelWatchState>> HitAsync(
        IDevicePlatform platform, DeviceTarget target, string pointId, CancellationToken ct) {
        if (!_watches.TryGetValue(target.Id, out var watch))
            return DeviceResult<PixelWatchState>.Error("no watch points are armed for this device");

        var point = watch.Snapshot().Find(p => p.Id == pointId);
        if (point is null) return DeviceResult<PixelWatchState>.Error("unknown watch point");
        if (watch.Paused) return DeviceResult<PixelWatchState>.Success(watch.State, "paused");
        if (!Ready(point)) return DeviceResult<PixelWatchState>.Success(watch.State, "cycling");

        await FireAsync(platform, watch, point, ct);
        if (point.Error is { } failed) return DeviceResult<PixelWatchState>.Error(failed);
        return DeviceResult<PixelWatchState>.Success(watch.State, "tapped");
    }

    public bool Remove(string deviceId, string pointId) {
        if (!_watches.TryGetValue(deviceId, out var watch)) return false;
        bool empty;
        bool removed;
        lock (watch.Gate) {
            removed = watch.Points.RemoveAll(p => p.Id == pointId) > 0;
            foreach (var g in watch.Groups) g.PointIds.Remove(pointId);
            watch.Groups.RemoveAll(g => g.PointIds.Count < 2);
            var live = watch.Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var p in watch.Points.Where(p => p.GroupId is { } gid && !live.Contains(gid))) p.GroupId = null;
            empty = watch.Points.Count == 0;
        }

        if (!removed) return false;
        if (empty) return StopAll(deviceId);
        Publish(deviceId, watch.State);
        return true;
    }

    public bool StopAll(string deviceId) {
        if (!_watches.TryRemove(deviceId, out var watch)) return false;
        _clientWatching.TryRemove(deviceId, out _);
        watch.Cts.Cancel();
        Publish(deviceId, PixelWatchState.Empty);
        return true;
    }

    private bool Ready(Point p) =>
        p.LastTapAt is not { } last || time.GetUtcNow() - last >= TimeSpan.FromMilliseconds(p.RateMs);

    private async Task LoopAsync(IDevicePlatform platform, Watch w) {
        var ct = w.Cts.Token;
        try {
            while (!ct.IsCancellationRequested) {
                await Task.Delay(PollEvery, ct);
                if (w.Paused || ClientWatching(w.Target.Id)) continue;
                var points = w.Snapshot().Where(p => w.IsLead(p) && Ready(p)).ToList();
                if (points.Count == 0) continue;

                var shot = await platform.ScreenshotAsync(w.Target, ct);
                if (!shot.Ok || shot.Value is not { Length: > 0 } png) {
                    foreach (var p in points) p.Error = shot.Note ?? "screenshot failed";
                    Publish(w.Target.Id, w.State);
                    continue;
                }

                var samples = PixelSampler.SampleMany(png, [.. points.Select(p => (p.X, p.Y))]);
                for (int i = 0; i < points.Count; i++) {
                    if (ct.IsCancellationRequested) break;
                    if (samples[i] is not { } px) continue;
                    if (!PixelSampler.Close(px, points[i].Color, Tolerance)) continue;
                    await FireAsync(platform, w, points[i], ct);
                }
            }
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "pixel watch for {Device} cancelled", w.Target.Id);
        } catch (Exception ex) {
            logger.LogWarning(ex, "pixel watch for {Device} stopped", w.Target.Id);
            foreach (var p in w.Snapshot()) p.Error = ex.Message;
            Publish(w.Target.Id, w.State);
        }
    }

    private async Task FireAsync(IDevicePlatform platform, Watch w, Point p, CancellationToken ct) {
        if (p.GroupId is { } gid && w.GroupById(gid) is { } group) {
            await FireGroupAsync(platform, w, group, ct);
            return;
        }

        await TapAsync(platform, w, p, ct);
    }

    private async Task FireGroupAsync(IDevicePlatform platform, Watch w, Group g, CancellationToken ct) {
        if (Interlocked.CompareExchange(ref g.Firing, 1, 0) != 0) return;
        try {
            var byId = w.Snapshot().ToDictionary(p => p.Id, StringComparer.Ordinal);
            var members = g.PointIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
            for (int i = 0; i < members.Count; i++) {
                if (ct.IsCancellationRequested || w.Paused) return;
                if (i > 0 && g.OffsetMs > 0) await Task.Delay(g.OffsetMs, ct);
                await TapAsync(platform, w, members[i], ct);
            }
        } finally {
            Volatile.Write(ref g.Firing, 0);
        }
    }

    private async Task TapAsync(IDevicePlatform platform, Watch w, Point p, CancellationToken ct) {
        Interlocked.Increment(ref w.TapDepth);
        Publish(w.Target.Id, w.State);
        try {
            var tap = await DeviceInputLane.RunAsync(w.Target.Id, token => PressAsync(platform, w.Target, p, token), ct);
            if (tap.Ok) {
                p.Taps++;
                p.LastTapAt = time.GetUtcNow();
                p.Error = null;
            } else {
                p.LastTapAt = time.GetUtcNow();
                p.Error = tap.Note ?? "tap failed";
            }
        } finally {
            Interlocked.Decrement(ref w.TapDepth);
        }

        Publish(w.Target.Id, w.State);
    }

    private static Task<DeviceResult> PressAsync(IDevicePlatform platform, DeviceTarget target, Point p,
        CancellationToken ct) {
        return p.Kind switch {
            PixelWatchKinds.Double => DoubleTapAsync(platform, target, p, ct),
            PixelWatchKinds.Hold => HoldAsync(platform, target, p, ct),
            _ => platform.TapPointAsync(target, p.X, p.Y, ct)
        };
    }

    private static async Task<DeviceResult> DoubleTapAsync(IDevicePlatform platform, DeviceTarget target, Point p,
        CancellationToken ct) {
        var first = await platform.TapPointAsync(target, p.X, p.Y, ct);
        if (!first.Ok) return first;
        await Task.Delay(DoubleGapMs, ct);
        return await platform.TapPointAsync(target, p.X, p.Y, ct);
    }

    private static async Task<DeviceResult> HoldAsync(IDevicePlatform platform, DeviceTarget target, Point p,
        CancellationToken ct) {
        var down = await platform.TouchAsync(target, TouchPhase.Down, p.X, p.Y, ct);
        if (down.Outcome == DeviceOutcome.Unsupported)
            return await platform.SwipeAsync(target, p.X, p.Y, p.X, p.Y, p.HoldMs, ct);
        if (!down.Ok) return down;
        try {
            await Task.Delay(p.HoldMs, ct);
        } finally {
            await platform.TouchAsync(target, TouchPhase.Up, p.X, p.Y, CancellationToken.None);
        }

        return DeviceResult.Success();
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
        public List<Group> Groups { get; } = [];
        public Lock Gate { get; } = new();
        public int TapDepth;
        public volatile bool Paused;

        public List<Point> Snapshot() {
            lock (Gate) return [.. Points];
        }

        public Group? GroupById(string id) {
            lock (Gate) return Groups.Find(g => g.Id == id);
        }

        public bool IsLead(Point p) {
            if (p.GroupId is not { } gid) return true;
            return GroupById(gid) is { } g && g.PointIds.Count > 0 && g.PointIds[0] == p.Id;
        }

        public PixelWatchState State {
            get {
                lock (Gate) {
                    return new PixelWatchState(
                        [.. Points.Select(p => p.Status)],
                        [.. Groups.Select(g => g.Status)],
                        Volatile.Read(ref TapDepth) > 0,
                        Paused);
                }
            }
        }
    }

    private sealed class Group(string color, int offsetMs, List<string> pointIds) {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..6];
        public string Color { get; } = color;
        public int OffsetMs { get; } = offsetMs;
        public List<string> PointIds { get; } = pointIds;
        public int Firing;

        public PixelWatchGroup Status => new(Id, Color, OffsetMs, [.. PointIds]);
    }

    private sealed class Point(int x, int y, SKColor color) {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
        public int X { get; } = x;
        public int Y { get; } = y;
        public SKColor Color { get; } = color;
        public string Kind { get; set; } = PixelWatchKinds.Tap;
        public int HoldMs { get; set; } = DefaultHoldMs;
        public int RateMs { get; set; } = DefaultRateMs;
        public string? GroupId { get; set; }
        public int Taps { get; set; }
        public DateTimeOffset? LastTapAt { get; set; }
        public string? Error { get; set; }

        public PixelWatchStatus Status =>
            new(Id, X, Y, PixelSampler.Hex(Color), Kind, HoldMs, RateMs, GroupId, Taps, LastTapAt, Error);
    }
}
