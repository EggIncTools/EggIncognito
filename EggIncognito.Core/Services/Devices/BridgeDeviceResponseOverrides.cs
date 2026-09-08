using System.Net.Http.Json;
using System.Text.Json;

namespace EggIncognito.Core.Services.Devices;

public sealed class BridgeDeviceResponseOverrides(IHttpClientFactory httpFactory, DeviceTransportConfig cfg)
    : IDeviceResponseOverrides {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DeviceResult> SetAsync(
        string deviceId, IReadOnlyList<DeviceResponseOverride> entries, CancellationToken ct) {
        var client = new BridgeClient(httpFactory.CreateClient(BridgeClient.HttpClientName), cfg);
        if (client.ConfigurationNote is { } missing) return DeviceResult.Unsupported(missing);

        try {
            using var req = client.Build(HttpMethod.Put, BridgeRoutes.OverridesFor(deviceId));
            req.Content = JsonContent.Create(new DeviceResponseOverrideSet(entries), options: JsonOptions);
            using var resp = await client.Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) {
                return DeviceResult.Error(
                    $"the host refused overrides on {deviceId} ({(int)resp.StatusCode} {resp.ReasonPhrase})");
            }

            return DeviceResult.Success($"installed {entries.Count} override(s) on the host for {deviceId}");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return DeviceResult.Unreachable($"bridge overrides error: {BridgeClient.Describe(ex)}");
        } catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) {
            return DeviceResult.Unreachable($"bridge overrides error: {BridgeClient.Describe(ex)}");
        }
    }

    public async Task<DeviceResult> ClearAsync(string deviceId, CancellationToken ct) {
        var client = new BridgeClient(httpFactory.CreateClient(BridgeClient.HttpClientName), cfg);
        if (client.ConfigurationNote is { } missing) return DeviceResult.Unsupported(missing);

        try {
            using var req = client.Build(HttpMethod.Delete, BridgeRoutes.OverridesFor(deviceId));
            using var resp = await client.Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) {
                return DeviceResult.Error(
                    $"the host refused to clear overrides on {deviceId} ({(int)resp.StatusCode} {resp.ReasonPhrase})");
            }

            return DeviceResult.Success($"cleared overrides on the host for {deviceId}");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return DeviceResult.Unreachable($"bridge overrides error: {BridgeClient.Describe(ex)}");
        } catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) {
            return DeviceResult.Unreachable($"bridge overrides error: {BridgeClient.Describe(ex)}");
        }
    }
}
