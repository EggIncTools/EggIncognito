using System.Collections.Concurrent;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;
using SixLabors.ImageSharp.PixelFormats;

namespace EggIncognito.Services.Devices;

public sealed class PixelWatchService(ILogger<PixelWatchService> logger) : IDisposable {
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan AfterTap = TimeSpan.FromMilliseconds(2500);
    public const int Tolerance = 48;

    private readonly ConcurrentDictionary<string, Watch> _watches = new(StringComparer.Ordinal);

    public event Action<string, PixelWatchStatus?>? Changed;

    public PixelWatchStatus? Status(string deviceId) => _watches.TryGetValue(deviceId, out var w) ? w.Status : null;

    public async Task<DeviceResult<PixelWatchStatus>> StartAsync(
        IDevicePlatform platform, DeviceTarget target, int x, int y, CancellationToken ct) {
        var shot = await platform.ScreenshotAsync(target, ct);
        if (!shot.Ok || shot.Value is not { Length: > 0 } png) {
            string note = shot.Note ?? "screenshot failed";
            return shot.Outcome == DeviceOutcome.Unreachable
                ? DeviceResult<PixelWatchStatus>.Unreachable(note)
                : DeviceResult<PixelWatchStatus>.Error(note);
        }

        if (PixelSampler.Sample(png, x, y) is not { } color)
            return DeviceResult<PixelWatchStatus>.Error("that point is outside the device screenshot");

        Stop(target.Id);
        var watch = new Watch(target, x, y, color);
        _watches[target.Id] = watch;
        watch.Loop = Task.Run(() => LoopAsync(platform, watch), CancellationToken.None);
        Publish(target.Id, watch.Status);
        return DeviceResult<PixelWatchStatus>.Success(watch.Status);
    }

    public bool Stop(string deviceId) {
        if (!_watches.TryRemove(deviceId, out var watch)) return false;
        watch.Cts.Cancel();
        Publish(deviceId, null);
        return true;
    }

    private async Task LoopAsync(IDevicePlatform platform, Watch w) {
        var ct = w.Cts.Token;
        try {
            await TapAsync(platform, w, ct);
            while (!ct.IsCancellationRequested) {
                await Task.Delay(Poll, ct);
                var shot = await platform.ScreenshotAsync(w.Target, ct);
                if (!shot.Ok || shot.Value is not { Length: > 0 } png) {
                    w.Error = shot.Note ?? "screenshot failed";
                    continue;
                }

                if (PixelSampler.Sample(png, w.X, w.Y) is not { } px || !PixelSampler.Close(px, w.Color, Tolerance)) continue;
                await TapAsync(platform, w, ct);
                await Task.Delay(AfterTap, ct);
            }
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "pixel watch for {Device} cancelled", w.Target.Id);
        } catch (Exception ex) {
            logger.LogWarning(ex, "pixel watch for {Device} stopped", w.Target.Id);
            w.Error = ex.Message;
            Publish(w.Target.Id, w.Status);
        }
    }

    private async Task TapAsync(IDevicePlatform platform, Watch w, CancellationToken ct) {
        var tap = await platform.TapPointAsync(w.Target, w.X, w.Y, ct);
        if (tap.Ok) {
            w.Taps++;
            w.LastTapAt = DateTimeOffset.UtcNow;
            w.Error = null;
        } else {
            w.Error = tap.Note ?? "tap failed";
        }

        Publish(w.Target.Id, w.Status);
    }

    private void Publish(string deviceId, PixelWatchStatus? status) {
        try {
            Changed?.Invoke(deviceId, status);
        } catch (Exception ex) {
            logger.LogDebug(ex, "pixel watch listener threw");
        }
    }

    public void Dispose() {
        foreach (var id in _watches.Keys.ToArray()) Stop(id);
    }

    private sealed class Watch(DeviceTarget target, int x, int y, Rgba32 color) {
        public DeviceTarget Target { get; } = target;
        public int X { get; } = x;
        public int Y { get; } = y;
        public Rgba32 Color { get; } = color;
        public CancellationTokenSource Cts { get; } = new();
        public Task? Loop { get; set; }
        public int Taps { get; set; }
        public DateTimeOffset? LastTapAt { get; set; }
        public string? Error { get; set; }

        public PixelWatchStatus Status => new(X, Y, PixelSampler.Hex(Color), Taps, LastTapAt, Error);
    }
}
