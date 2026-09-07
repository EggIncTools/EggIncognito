using System.Net.Sockets;
using System.Threading.Channels;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Admin;

namespace EggIncognito.Services.Devices;

public sealed class DockerEventWatcher(
    DockerEngineClient docker,
    VirtualDeviceConfig config,
    VirtualDeviceLifecycle lifecycle,
    AdminNotifier notifier,
    TimeProvider time,
    ILogger<DockerEventWatcher> logger) : BackgroundService {
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StableFor = TimeSpan.FromSeconds(30);
    private static readonly string[] WatchedActions = ["start", "die", "stop", "destroy"];
    private const string HealthPrefix = "health_status";

    private readonly Channel<byte> _signal = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public static bool IsWatched(string action) =>
        action.StartsWith(HealthPrefix, StringComparison.Ordinal)
        || WatchedActions.Contains(action, StringComparer.Ordinal);

    public static TimeSpan NextBackoff(TimeSpan current) {
        var doubled = current + current;
        return doubled > MaxBackoff ? MaxBackoff : doubled;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!config.Enabled) {
            logger.LogInformation("docker events: idle, virtual devices are disabled");
            return;
        }

        if (!docker.Available) {
            logger.LogInformation("docker events: idle, {Endpoint} is not usable", docker.Endpoint.Describe());
            return;
        }

        var pump = PumpAsync(stoppingToken);
        var backoff = MinBackoff;
        while (!stoppingToken.IsCancellationRequested) {
            var openedAt = time.GetUtcNow();
            bool connected = await ReadStreamAsync(stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;
            if (connected && time.GetUtcNow() - openedAt >= StableFor) backoff = MinBackoff;

            try {
                await Task.Delay(backoff, time, stoppingToken);
            } catch (OperationCanceledException) {
                break;
            }

            backoff = NextBackoff(backoff);
        }

        _signal.Writer.TryComplete();
        await pump;
    }

    private async Task<bool> ReadStreamAsync(CancellationToken ct) {
        var opened = await docker.OpenEventsAsync(RedroidProvisioner.OwnerFilter, ct);
        if (!opened.Ok || opened.Value is not { } stream) {
            logger.LogDebug("docker events: cannot subscribe ({Outcome}): {Note}",
                DeviceOutcomes.Label(opened.Outcome), opened.Note ?? "no detail");
            return false;
        }

        await using (stream) {
            logger.LogInformation("docker events: watching container events for {Filter}",
                RedroidProvisioner.OwnerFilter);
            await ReconcileAsync(ct);
            try {
                while (await stream.ReadAsync(ct) is { } ev) {
                    if (!IsWatched(ev.Action)) continue;
                    logger.LogDebug("docker events: {Action} on {Container}", ev.Action,
                        ev.Name.Length > 0 ? ev.Name : ev.Id);
                    _signal.Writer.TryWrite(0);
                }
            } catch (OperationCanceledException) {
                return true;
            } catch (Exception ex) when (ex is IOException or HttpRequestException or SocketException
                                             or ObjectDisposedException) {
                logger.LogDebug(ex, "docker events: stream dropped, reconnecting");
            }
        }

        return true;
    }

    private async Task PumpAsync(CancellationToken ct) {
        try {
            while (await _signal.Reader.WaitToReadAsync(ct)) {
                _signal.Reader.TryRead(out _);
                await Task.Delay(CoalesceWindow, time, ct);
                while (_signal.Reader.TryRead(out _)) { }
                Publish();
                await ReconcileAsync(ct);
            }
        } catch (OperationCanceledException) {
        } catch (ChannelClosedException) {
        }
    }

    private void Publish() {
        try {
            notifier.Publish(AdminTopics.VirtualDevices);
        } catch (Exception ex) {
            logger.LogWarning(ex, "docker events: a subscriber threw while handling a fleet change");
        }
    }

    private async Task ReconcileAsync(CancellationToken ct) {
        try {
            await lifecycle.ReconcileAsync(ct);
        } catch (OperationCanceledException) {
        }
    }
}
