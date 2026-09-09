using System.Globalization;
using EggIncognito.Core.Services.Devices;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Services.Devices;

public sealed class DeviceStreamHold : IAsyncDisposable {
    public const int StreamingScreenTimeoutMs = 1_800_000;
    public static readonly TimeSpan MinClaimTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReleaseBudget = TimeSpan.FromSeconds(10);

    private readonly IDeviceClaims? _claims;
    private readonly IDeviceConnection? _conn;
    private readonly string _deviceId;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _renewals = new();
    private Task? _renewal;
    private string? _previousTimeout;

    public DeviceStreamHold(IDeviceClaims? claims, IDeviceConnection? conn, string deviceId, ILogger logger) {
        _claims = claims;
        _conn = conn;
        _deviceId = deviceId;
        _logger = logger;
    }

    public static async Task<DeviceStreamHold> AcquireAsync(
        IServiceProvider services, DeviceTarget target, CancellationToken ct) {
        var claims = services.GetService(typeof(IDeviceClaims)) as IDeviceClaims;
        var factory = services.GetService(typeof(IDeviceConnectionFactory)) as IDeviceConnectionFactory;
        var logger = services.GetService(typeof(ILogger<DeviceStreamHold>)) as ILogger ?? NullLogger.Instance;
        var conn = Platforms.Matches(target.Platform, Platforms.Android) ? factory?.For(target) : null;
        var hold = new DeviceStreamHold(claims, conn, target.Id, logger);

        if (claims is { Active: true }) {
            var ttl = TtlOf(services);
            var claim = await claims.ClaimAsync(target.Id, ttl, ct);
            if (!claim.Ok) logger.LogWarning("stream: claiming {Device} failed: {Note}", target.Id, claim.Note);
            else hold._renewal = hold.RenewAsync(claims, ttl);
        }

        await hold.KeepAwakeAsync(ct);
        return hold;
    }

    private static TimeSpan TtlOf(IServiceProvider services) {
        var seconds = (services.GetService(typeof(DeviceTransportConfig)) as DeviceTransportConfig)?.ClaimTtlSeconds ?? 0;
        var ttl = TimeSpan.FromSeconds(seconds);
        return ttl < MinClaimTtl ? MinClaimTtl : ttl;
    }

    private async Task RenewAsync(IDeviceClaims claims, TimeSpan ttl) {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(ttl.TotalSeconds / 2));
        try {
            while (await timer.WaitForNextTickAsync(_renewals.Token)) {
                var renewed = await claims.ClaimAsync(_deviceId, ttl, _renewals.Token);
                if (!renewed.Ok) _logger.LogWarning("stream: re-claiming {Device} failed: {Note}", _deviceId, renewed.Note);
            }
        } catch (OperationCanceledException ex) {
            _logger.LogDebug(ex, "stream: claim renewal for {Device} stopped", _deviceId);
        }
    }

    private async Task KeepAwakeAsync(CancellationToken ct) {
        if (_conn is null) return;
        try {
            var previous = await _conn.ShellAsync("settings get system screen_off_timeout", ct);
            if (previous.ExitCode == 0 && int.TryParse(previous.Stdout.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                _previousTimeout = previous.Stdout.Trim();
            await _conn.ShellAsync("input keyevent KEYCODE_WAKEUP", ct);
            await _conn.ShellAsync("wm dismiss-keyguard", ct);
            await _conn.ShellAsync("svc power stayon true", ct);
            await _conn.ShellAsync($"settings put system screen_off_timeout {StreamingScreenTimeoutMs}", ct);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogDebug(ex, "stream: keep-awake for {Device} failed", _deviceId);
        }
    }

    public async ValueTask DisposeAsync() {
        await _renewals.CancelAsync();
        if (_renewal is not null) await _renewal;
        _renewals.Dispose();

        using var budget = new CancellationTokenSource(ReleaseBudget);
        if (_conn is not null) {
            try {
                if (_previousTimeout is not null)
                    await _conn.ShellAsync($"settings put system screen_off_timeout {_previousTimeout}", budget.Token);
                await _conn.ShellAsync("svc power stayon false", budget.Token);
            } catch (Exception ex) when (ex is not OutOfMemoryException) {
                _logger.LogDebug(ex, "stream: restoring screen timeout for {Device} failed", _deviceId);
            }
        }

        if (_claims is { Active: true }) {
            try {
                await _claims.ReleaseAsync(_deviceId, budget.Token);
            } catch (Exception ex) when (ex is not OutOfMemoryException) {
                _logger.LogDebug(ex, "stream: releasing claim on {Device} failed", _deviceId);
            }
        }
    }
}
