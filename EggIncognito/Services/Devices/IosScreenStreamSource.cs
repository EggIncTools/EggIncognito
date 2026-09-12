using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices.QuickTime;

namespace EggIncognito.Services.Devices;

public sealed class IosScreenStreamSource(ILogger<IosScreenStreamSource> log) : IScreenStreamSource {
    private const int IdleTimeoutSeconds = 20;

    public string Platform => Platforms.Ios;

    public async Task<string?> StreamAsync(DeviceTarget target, ScreenStreamOptions options, Stream output,
        CancellationToken ct) {
        QtUsbDevice? usb;
        try {
            usb = await QtUsbDevice.OpenAsync(target.Target, ct);
        } catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) {
            return $"quicktime usb open failed: {ex.Message}";
        }

        if (usb is null) return "no quicktime av interface on this device";

        using (usb) {
            var session = new QtSession((data, token) => usb.SendAsync(data, token), output,
                options.Width, options.Height);
            var idle = TimeProvider.System.GetTimestamp();
            try {
                while (!ct.IsCancellationRequested) {
                    byte[]? packet = usb.ReadPacket(ct);
                    if (packet is null) {
                        if (TimeProvider.System.GetElapsedTime(idle).TotalSeconds > IdleTimeoutSeconds)
                            return session.Frames == 0
                                ? "quicktime session produced no frames"
                                : "quicktime stream stalled";
                        continue;
                    }
                    idle = TimeProvider.System.GetTimestamp();
                    await session.HandleAsync(packet, ct);
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) when (ex is IOException or ObjectDisposedException) {
                return session.Frames == 0 ? $"quicktime stream failed: {ex.Message}" : null;
            } finally {
                await CloseQuietlyAsync(session, target.Id);
            }
        }
        return null;
    }

    private async Task CloseQuietlyAsync(QtSession session, string deviceId) {
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await session.CloseAsync(cts.Token);
        } catch (Exception ex) when (ex is IOException or OperationCanceledException
                                        or ObjectDisposedException or InvalidOperationException) {
            log.LogDebug(ex, "quicktime session close for {DeviceId} did not complete", deviceId);
        }
    }
}
