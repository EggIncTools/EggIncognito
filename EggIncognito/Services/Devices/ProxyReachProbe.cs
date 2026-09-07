using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class ProxyReachProbe(
    IHttpClientFactory httpFactory,
    DeviceTransportConfig transport,
    DeviceCaptureConfig capture,
    DeviceProxyPusher pusher) {
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan BridgeTimeout = TimeSpan.FromSeconds(15);

    public string? Host => pusher.HostIp;

    public Task<DeviceResult> CheckAsync(CancellationToken ct) => CheckAsync(null, ct);

    public async Task<DeviceResult> CheckAsync(string? deviceId, CancellationToken ct) {
        if (transport.Mode == DeviceTransportMode.Remote) return await RemoteAsync(deviceId, ct);

        if (Host is not { Length: > 0 } host)
            return DeviceResult.Unsupported("no capture proxy address to test (set DeviceCapture:HostIp)");

        int port = PortFor(deviceId);
        if (port <= 0) return DeviceResult.Unsupported("no capture listener port to test");
        return await LocalAsync(host, port, ct);
    }

    private async Task<DeviceResult> RemoteAsync(string? deviceId, CancellationToken ct) {
        if (await pusher.HostIpAsync(ct) is not { Length: > 0 } host)
            return DeviceResult.Unsupported("the host bridge reports no capture host ip");
        if (deviceId is not { Length: > 0 } id)
            return DeviceResult.Unsupported("no device to look a capture port up for");

        int port = await pusher.PortForAsync(id, ct);
        if (port <= 0) return DeviceResult.Unsupported("the host bridge reports no capture port for this device");
        return await BridgeAsync(host, port, ct);
    }

    private int PortFor(string? deviceId) {
        if (deviceId is not { Length: > 0 } id) return capture.BasePort;
        int mapped = pusher.PortFor(id);
        return mapped > 0 ? mapped : capture.BasePort;
    }

    private async Task<DeviceResult> BridgeAsync(string host, int port, CancellationToken ct) {
        var client = new BridgeClient(httpFactory.CreateClient(BridgeClient.HttpClientName), transport);
        if (client.ConfigurationNote is { } missing) return DeviceResult.Unsupported(missing);

        string query = $"host={Uri.EscapeDataString(host)}&port={port.ToString(CultureInfo.InvariantCulture)}";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(BridgeTimeout);
        try {
            using var request = client.Build(HttpMethod.Get, BridgeRoutes.Reach, query);
            using var response = await client.Http.SendAsync(request, cts.Token);
            string body = await response.Content.ReadAsStringAsync(cts.Token);
            if (!response.IsSuccessStatusCode) {
                return DeviceResult.Error(
                    $"the host bridge refused the reach probe ({(int)response.StatusCode}): {DeviceParsing.TrimNote(body)}");
            }

            var (ok, note) = Parse(body);
            return ok
                ? DeviceResult.Success(note ?? $"the host reaches {host}:{port}")
                : DeviceResult.Error(note ?? $"the host cannot reach {host}:{port}");
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return DeviceResult.Unreachable($"the host bridge did not answer the reach probe for {host}:{port}");
        } catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException) {
            return DeviceResult.Unreachable($"the host bridge is unreachable: {BridgeClient.Describe(ex)}");
        }
    }

    private static (bool Ok, string? Note) Parse(string body) {
        try {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            bool ok = root.TryGetProperty("ok", out var flag) && flag.ValueKind == JsonValueKind.True;
            string? note = root.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()
                : null;
            return (ok, string.IsNullOrWhiteSpace(note) ? null : note);
        } catch (JsonException) {
            return (false, $"the host bridge answered with something other than json: {DeviceParsing.TrimNote(body)}");
        }
    }

    private static async Task<DeviceResult> LocalAsync(string host, int port, CancellationToken ct) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ConnectTimeout);
        using var client = new TcpClient();
        try {
            await client.ConnectAsync(host, port, cts.Token);
            return DeviceResult.Success($"{host}:{port} accepts connections");
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return DeviceResult.Unreachable(
                $"{host}:{port} did not accept a connection within {ConnectTimeout.TotalSeconds:F0}s");
        } catch (SocketException ex) {
            return DeviceResult.Unreachable($"{host}:{port} is not reachable: {ex.Message}");
        }
    }
}
