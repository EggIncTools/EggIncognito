using EggIdentity.Hosting;
using EggIncognito.Capture;

namespace EggIncognito.Services;

public sealed class CaptureSweeper : PeriodicScopedService {
    private readonly CaptureSessionManager _manager;
    private readonly HostedCaptureOptions _opts;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    public CaptureSweeper(IServiceScopeFactory scopeFactory, CaptureSessionManager manager, HostedCaptureOptions opts,
        TimeProvider time, ILogger<CaptureSweeper> logger) : base(scopeFactory, time, logger) {
        _manager = manager;
        _opts = opts;
        _time = time;
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

    protected override bool RunAtStart => false;

    protected override Task RunOnceAsync(IServiceProvider services, CancellationToken ct) => SweepOnceAsync(_time.GetUtcNow());

    internal async Task SweepOnceAsync(DateTimeOffset nowUtc) {
        foreach ((string key, var session) in _manager.All()) {
            if (key == CaptureSessionManager.LocalKey) continue;

            bool idle = nowUtc - session.LastFlowUtc > TimeSpan.FromMinutes(_opts.MaxIdleMinutes);
            bool capped = nowUtc - session.StartedUtc > TimeSpan.FromHours(_opts.MaxSessionHours);

            if (session.State != CaptureState.Running) {
                if (session.State == CaptureState.Stopped && idle) {
                    _manager.Remove(key);
                    _logger.LogInformation("capture sweep: released stopped session {Key}", key);
                }

                continue;
            }

            if (!idle && !capped) continue;

            _manager.Remove(key);
            try {
                await session.StopAsync();
            } catch (Exception ex) {
                _logger.LogWarning(ex, "capture sweep: stop failed for {Key}", key);
            }

            _logger.LogInformation("capture sweep: stopped {Key} ({Reason})", key, capped ? "session cap" : "idle");
        }
    }
}
