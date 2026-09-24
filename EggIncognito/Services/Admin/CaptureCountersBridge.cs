using EggIncognito.Capture;
using EggIncognito.Services.Devices;

namespace EggIncognito.Services.Admin;

public sealed class CaptureCountersBridge : IHostedService, IDisposable {
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    private readonly DeviceCaptureManager? _devices;
    private readonly ILogger<CaptureCountersBridge> _logger;
    private readonly AdminNotifier _notifier;
    private readonly CaptureSessionManager _sessions;
    private readonly Timer _timer;

    private int _armed;

    public CaptureCountersBridge(AdminNotifier notifier, CaptureSessionManager sessions,
        DeviceCaptureManager? devices, ILogger<CaptureCountersBridge> logger) {
        _notifier = notifier;
        _sessions = sessions;
        _devices = devices;
        _logger = logger;
        _timer = new Timer(_ => Publish(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Dispose() => _timer.Dispose();

    public Task StartAsync(CancellationToken cancellationToken) {
        _sessions.StatsChanged += Signal;
        _devices?.CountersChanged += Signal;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        _sessions.StatsChanged -= Signal;
        _devices?.CountersChanged -= Signal;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    private void Signal() {
        if (Interlocked.Exchange(ref _armed, 1) == 1) return;
        try {
            _timer.Change(Window, Timeout.InfiniteTimeSpan);
        } catch (ObjectDisposedException) {
            Interlocked.Exchange(ref _armed, 0);
        }
    }

    private void Publish() {
        Interlocked.Exchange(ref _armed, 0);
        try {
            _notifier.Publish(AdminTopics.Sessions);
        } catch (Exception ex) {
            _logger.LogDebug(ex, "capture counters: sessions publish threw");
        }
    }
}
