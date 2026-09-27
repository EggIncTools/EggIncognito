using System.Globalization;
using EggIncognito.Core.Services.Devices;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Services.Devices;

public sealed class DeviceStreamHold(IDeviceClaims? claims, IDeviceConnection? conn, string deviceId, ILogger logger)
    : IAsyncDisposable {
    public const int StreamingScreenTimeoutMs = 1_800_000;
    public static readonly TimeSpan MinClaimTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReleaseBudget = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _renewals = new();
    private Task? _renewal;
    private string? _previousTimeout;

    public static async Task<DeviceStreamHold> AcquireAsync(
        IServiceProvider services, DeviceTarget target, CancellationToken ct) {
        var claims = services.GetService(typeof(IDeviceClaims)) as IDeviceClaims;
        var factory = services.GetService(typeof(IDeviceConnectionFactory)) as IDeviceConnectionFactory;
        var logger = services.GetService(typeof(ILogger<DeviceStreamHold>)) as ILogger ?? NullLogger.Instance;
        var conn = Platforms.Matches(target.Platform, Platforms.Android) ? factory?.For(target) : null;
        var hold = new DeviceStreamHold(claims, conn, target.Id, logger);

        var awake = hold.KeepAwakeAsync(ct);
        if (claims is { Active: true }) {
            var ttl = TtlOf(services);
            var claim = await claims.ClaimAsync(target.Id, ttl, ct);
            if (!claim.Ok) logger.LogWarning("stream: claiming {Device} failed: {Note}", target.Id, claim.Note);
            else hold._renewal = hold.RenewAsync(claims, ttl);
        }

        await awake;
        return hold;
    }

    private static TimeSpan TtlOf(IServiceProvider services) {
        var seconds = (services.GetService(typeof(DeviceTransportConfig)) as DeviceTransportConfig)?.ClaimTtlSeconds ?? 0;
        var ttl = TimeSpan.FromSeconds(seconds);
        return ttl < MinClaimTtl ? MinClaimTtl : ttl;
    }

    private async Task RenewAsync(IDeviceClaims renewClaims, TimeSpan ttl) {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(ttl.TotalSeconds / 2));
        try {
            while (await timer.WaitForNextTickAsync(_renewals.Token)) {
                var renewed = await renewClaims.ClaimAsync(deviceId, ttl, _renewals.Token);
                if (!renewed.Ok) logger.LogWarning("stream: re-claiming {Device} failed: {Note}", deviceId, renewed.Note);
            }
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "stream: claim renewal for {Device} stopped", deviceId);
        }
    }

    public static string KeepAwakeCommand =>
        "settings get system screen_off_timeout; input keyevent KEYCODE_WAKEUP; wm dismiss-keyguard; "
        + $"svc power stayon true; settings put system screen_off_timeout {StreamingScreenTimeoutMs}";

    public static string RestoreCommand(string? previousTimeout) =>
        previousTimeout is null
            ? "svc power stayon false"
            : $"settings put system screen_off_timeout {previousTimeout}; svc power stayon false";

    public static string? PreviousTimeout(string stdout) {
        string first = stdout.Split('\n', 2)[0].Trim();
        return int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? first : null;
    }

    private async Task KeepAwakeAsync(CancellationToken ct) {
        if (conn is null) return;
        try {
            var r = await conn.ShellAsync(KeepAwakeCommand, ct);
            _previousTimeout = PreviousTimeout(r.Stdout);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogDebug(ex, "stream: keep-awake for {Device} failed", deviceId);
        }
    }

    public async ValueTask DisposeAsync() {
        await _renewals.CancelAsync();
        if (_renewal is not null) await _renewal;
        _renewals.Dispose();

        using var budget = new CancellationTokenSource(ReleaseBudget);
        if (conn is not null) {
            try {
                await conn.ShellAsync(RestoreCommand(_previousTimeout), budget.Token);
            } catch (Exception ex) when (ex is not OutOfMemoryException) {
                logger.LogDebug(ex, "stream: restoring screen timeout for {Device} failed", deviceId);
            }
        }

        if (claims is { Active: true }) {
            try {
                await claims.ReleaseAsync(deviceId, budget.Token);
            } catch (Exception ex) when (ex is not OutOfMemoryException) {
                logger.LogDebug(ex, "stream: releasing claim on {Device} failed", deviceId);
            }
        }
    }
}
