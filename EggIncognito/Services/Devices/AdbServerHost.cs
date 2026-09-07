using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class AdbServerHost(IProcessRunner runner, ILogger<AdbServerHost> logger) : BackgroundService, IAdbServer {
    public const string SocketEnv = "ADB_SERVER_SOCKET";
    public const string DefaultSocket = "tcp:127.0.0.1:5037";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ProcessHandle? _server;
    private DateTimeOffset? _since;

    public string Socket { get; } = Environment.GetEnvironmentVariable(SocketEnv) is { Length: > 0 } s ? s : DefaultSocket;

    public bool Owned => _server is not null;

    public string Describe() => _server is null
        ? $"adb server on {Socket} is not owned by this app"
        : $"adb server on {Socket} owned by this app since {_since:HH:mm:ss}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var backoff = MinBackoff;
        try {
            while (!stoppingToken.IsCancellationRequested) {
                if (_server is { } live) {
                    int code = await live.Exited.WaitAsync(stoppingToken);
                    logger.LogWarning("adb server: exited with {Code}: {Tail}", code, live.StderrTail());
                    await ReleaseAsync();
                    await Task.Delay(backoff, stoppingToken);
                    backoff = backoff < MaxBackoff ? backoff * 2 : MaxBackoff;
                }

                if (await ReachableAsync(stoppingToken)) {
                    backoff = MinBackoff;
                    await Task.Delay(CheckInterval, stoppingToken);
                    continue;
                }

                await SpawnAsync(stoppingToken);
                if (_server is null) await Task.Delay(backoff, stoppingToken);
            }
        } catch (OperationCanceledException) {
            await ReleaseAsync();
        }
    }

    public async Task RestartAsync(CancellationToken ct) {
        await _gate.WaitAsync(ct);
        try {
            if (_server is not null) {
                logger.LogWarning("adb server: restarting the server this app owns");
                await ReleaseAsync();
            } else {
                logger.LogWarning("adb server: killing the external server on {Socket} and taking ownership", Socket);
                await Run(["kill-server"], ct);
            }

            await SpawnAsync(ct);
        } finally {
            _gate.Release();
        }
    }

    private async Task<bool> ReachableAsync(CancellationToken ct) {
        var r = await Run(["devices"], ct);
        if (r.ExitCode == 0) return true;
        string text = r.Stderr + r.Stdout;
        return !text.Contains("cannot connect to daemon", StringComparison.OrdinalIgnoreCase)
               && !text.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
               && !text.Contains("cannot start server", StringComparison.OrdinalIgnoreCase);
    }

    private async Task SpawnAsync(CancellationToken ct) {
        ProcessHandle handle;
        try {
            handle = await runner.StartAsync("adb", ["-L", Socket, "server", "nodaemon"], CancellationToken.None);
        } catch (NotSupportedException ex) {
            logger.LogWarning(ex, "adb server: this process runner cannot host a server");
            return;
        }

        if (handle.Exited.IsCompleted) {
            logger.LogWarning("adb server: failed to start on {Socket}: {Tail}", Socket, handle.StderrTail());
            await handle.DisposeAsync();
            return;
        }

        _server = handle;
        _since = DateTimeOffset.UtcNow;
        _ = handle.Stdout.CopyToAsync(Stream.Null, CancellationToken.None);
        logger.LogInformation("adb server: started on {Socket} from this process; it signs with this container's /root/.android key", Socket);
        for (int i = 0; i < 20 && !await ReachableAsync(ct); i++) await Task.Delay(250, ct);
    }

    private async Task ReleaseAsync() {
        if (_server is not { } live) return;
        _server = null;
        _since = null;
        await live.DisposeAsync();
    }

    private async Task<ProcessResult> Run(string[] args, CancellationToken ct) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProbeTimeout);
        return await runner.RunAsync("adb", args, cts.Token);
    }

    public override void Dispose() {
        base.Dispose();
        _gate.Dispose();
    }
}
