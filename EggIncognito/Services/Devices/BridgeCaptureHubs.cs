using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class BridgeCaptureHubs(
    IHttpClientFactory httpFactory, DeviceTransportConfig cfg, ILogger<BridgeCaptureHubs> logger)
    : IDeviceCaptureHubs, IDisposable {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<string, Lazy<CaptureHub>> _hubs = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();

    public CaptureHub? HubFor(string deviceId) =>
        _hubs.GetOrAdd(deviceId, id => new Lazy<CaptureHub>(() => Start(id))).Value;

    public void Dispose() => _cts.Cancel();

    private CaptureHub Start(string deviceId) {
        var hub = new CaptureHub();
        _ = Task.Run(() => MirrorAsync(deviceId, hub, _cts.Token), CancellationToken.None);
        return hub;
    }

    private async Task MirrorAsync(string deviceId, CaptureHub hub, CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            bool again;
            try {
                again = await StreamOnceAsync(deviceId, hub, ct);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                return;
            } catch (Exception ex) {
                logger.LogDebug(ex, "device capture mirror: {Id} stream broke", deviceId);
                again = true;
            }

            hub.Mirror(new CaptureEnvelope("stats", null,
                hub.StatsSnapshot() with { Running = false, ActiveConnections = 0 }, null));
            if (!again) {
                _hubs.TryRemove(deviceId, out _);
                return;
            }

            try {
                await Task.Delay(Retry, ct);
            } catch (OperationCanceledException) {
                return;
            }
        }
    }

    private async Task<bool> StreamOnceAsync(string deviceId, CaptureHub hub, CancellationToken ct) {
        var client = new BridgeClient(httpFactory.CreateClient(BridgeClient.HttpClientName), cfg);
        if (client.ConfigurationNote is { } missing) {
            logger.LogWarning("device capture mirror: {Id} cannot start, {Why}", deviceId, missing);
            return false;
        }

        using var req = client.Build(HttpMethod.Get, BridgeRoutes.CaptureFor(deviceId));
        using var resp = await client.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) {
            logger.LogInformation("device capture mirror: {Id} has no capture on the host", deviceId);
            return false;
        }

        if (!resp.IsSuccessStatusCode) {
            logger.LogWarning("device capture mirror: {Id} bridge {Status} {Reason}", deviceId,
                (int)resp.StatusCode, resp.ReasonPhrase);
            return true;
        }

        await using var body = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(body);
        while (await reader.ReadLineAsync(ct) is { } line) {
            if (line.Length == 0) continue;
            if (JsonSerializer.Deserialize<CaptureEnvelope>(line, Json) is { } env) hub.Mirror(env);
        }

        return true;
    }
}
