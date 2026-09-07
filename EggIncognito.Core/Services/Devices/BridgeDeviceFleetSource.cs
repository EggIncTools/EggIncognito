using System.Net.Http.Json;
using System.Text.Json;

namespace EggIncognito.Core.Services.Devices;

public sealed class BridgeDeviceFleetSource(
    IHttpClientFactory httpFactory, DeviceTransportConfig cfg, TimeProvider time) {
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Lock _gate = new();
    private BridgeFleet? _cached;
    private DateTimeOffset _cachedAt;

    public void Invalidate() {
        lock (_gate) _cached = null;
    }

    public async Task<DeviceResult<BridgeFleet>> GetAsync(CancellationToken ct) {
        if (Fresh() is { } cached) return DeviceResult<BridgeFleet>.Success(cached);

        var client = new BridgeClient(httpFactory.CreateClient(BridgeClient.HttpClientName), cfg);
        if (client.ConfigurationNote is { } missing) return DeviceResult<BridgeFleet>.Unsupported(missing);

        try {
            using var req = client.Build(HttpMethod.Get, BridgeRoutes.Fleet);
            using var resp = await client.Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return DeviceResult<BridgeFleet>.Unreachable($"bridge fleet {(int)resp.StatusCode} {resp.ReasonPhrase}");

            var fleet = await resp.Content.ReadFromJsonAsync<BridgeFleet>(JsonOptions, ct);
            if (fleet is null) return DeviceResult<BridgeFleet>.Unreachable("bridge fleet returned an empty response");

            lock (_gate) {
                _cached = fleet;
                _cachedAt = time.GetUtcNow();
            }

            return DeviceResult<BridgeFleet>.Success(fleet);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return DeviceResult<BridgeFleet>.Unreachable($"bridge fleet error: {BridgeClient.Describe(ex)}");
        } catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) {
            return DeviceResult<BridgeFleet>.Unreachable($"bridge fleet error: {BridgeClient.Describe(ex)}");
        }
    }

    private BridgeFleet? Fresh() {
        lock (_gate) {
            return _cached is { } cached && time.GetUtcNow() - _cachedAt < CacheFor ? cached : null;
        }
    }
}
