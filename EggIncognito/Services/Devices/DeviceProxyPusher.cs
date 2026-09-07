using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class DeviceProxyPusher(
    DeviceCaptureManager manager,
    DeviceCaptureConfig config,
    IDevicePlatforms platforms,
    ILogger<DeviceProxyPusher> logger) {
    private bool _warnedBridge;

    public string? HostIp => HostAddress.Resolve(config.HostIp);

    public int PortFor(string deviceId) => manager.PortFor(deviceId);

    private static DeviceTarget TargetOf(DeviceEntry d) => new(d.Id, d.Platform, d.Target, d.Package);

    public async Task PushAllAsync(IReadOnlyList<DeviceEntry> devices, CancellationToken ct) {
        if (!config.Enabled) return;
        string? host = HostIp;
        if (string.IsNullOrEmpty(host)) {
            logger.LogWarning("device capture: cannot push proxy, host IP unresolved (set DeviceCapture:HostIp)");
            return;
        }

        if (!_warnedBridge && string.IsNullOrWhiteSpace(config.HostIp) && LooksLikeDockerBridge(host)) {
            _warnedBridge = true;
            logger.LogWarning(
                "device capture: auto-detected host IP {Host} looks like a docker bridge address - LAN devices " +
                "cannot reach it, so no traffic will be captured. Pin DeviceCapture:HostIp to the host's LAN IP.",
                host);
        }

        foreach (var d in devices) await PushOneAsync(d, host, ct);
    }

    internal static bool LooksLikeDockerBridge(string ip) {
        string[] p = ip.Split('.');
        return p.Length == 4 && p[0] == "172" && int.TryParse(p[1], out int b) && b >= 16 && b <= 31;
    }

    public async Task<(bool Ok, string? Note)> PushOneAsync(DeviceEntry d, string host, CancellationToken ct) {
        int port = manager.PortFor(d.Id);
        if (port == 0) return (false, "no capture listener for device");

        var res = await platforms.For(d.Platform).SetProxyAsync(TargetOf(d), host, port, ct);
        if (res.Ok)
            logger.LogInformation("device capture: {Id} proxy -> {Host}:{Port}{Note}", d.Id, host, port,
                res.Note is null ? "" : $" ({res.Note})");
        else logger.LogWarning("device capture: {Id} proxy push failed: {Note}", d.Id, res.Note);
        return (res.Ok, res.Note);
    }

    public DeviceRinfo? LastRinfo(string deviceId) => manager.Rinfo.Latest(deviceId);

    public async Task<DeviceRinfo?> ForceHarvestAsync(
        DeviceEntry d, TimeSpan timeout, CancellationToken ct, TimeSpan settle = default) {
        var before = manager.Rinfo.Latest(d.Id);
        await RestartAppAsync(d, ct);

        var deadline = DateTimeOffset.UtcNow + timeout;
        DeviceRinfo? result = null;
        while (DateTimeOffset.UtcNow < deadline) {
            try {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            } catch (OperationCanceledException) {
                break;
            }

            var now = manager.Rinfo.Latest(d.Id);
            if (now is not null && (before is null || now.LastSeen != before.LastSeen)) {
                result = now;
                break;
            }
        }

        if (settle > TimeSpan.Zero) {
            try {
                await Task.Delay(settle, ct);
            } catch (OperationCanceledException ex) {
                logger.LogDebug(ex, "device capture: {Id} settle delay cancelled", d.Id);
            }
        }

        try {
            await LockDeviceAsync(d, ct);
        } catch (Exception ex) {
            logger.LogDebug(ex, "device {Id} relock failed (non-fatal)", d.Id);
        }

        return result ?? manager.Rinfo.Latest(d.Id);
    }

    public async Task<(bool Ok, string? Note)> LockDeviceAsync(DeviceEntry d, CancellationToken ct) {
        var res = await platforms.For(d.Platform).LockAsync(TargetOf(d), ct);
        return (res.Ok, res.Note);
    }

    public async Task<(bool Ok, string? Note)> RestartAppAsync(DeviceEntry d, CancellationToken ct) {
        try {
            var res = await platforms.For(d.Platform).RestartAppAsync(TargetOf(d), ct);
            logger.LogInformation("device capture: {Id} app restart ({Outcome}): {Note}", d.Id, res.Outcome, res.Note);
            return (res.Ok, res.Note);
        } catch (Exception ex) {
            logger.LogDebug(ex, "device capture: {Id} app restart failed (non-fatal)", d.Id);
            return (false, ex.Message);
        }
    }
}
