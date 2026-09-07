using System.Net.Http.Json;
using System.Text.Json;

namespace EggIncognito.Core.Services.Devices;

public sealed class BridgeHostFacts(IHttpClientFactory httpFactory, DeviceTransportConfig cfg) : IHostFacts {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private HostFacts? _cached;

    public async Task<DeviceResult<HostFacts>> GetAsync(CancellationToken ct) {
        if (_cached is { } cached) return DeviceResult<HostFacts>.Success(cached);

        var client = new BridgeClient(httpFactory.CreateClient(BridgeClient.HttpClientName), cfg);
        if (client.ConfigurationNote is { } missing) return DeviceResult<HostFacts>.Unsupported(missing);

        try {
            using var req = client.Build(HttpMethod.Get, BridgeRoutes.Host);
            using var resp = await client.Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return DeviceResult<HostFacts>.Unreachable($"bridge host {(int)resp.StatusCode} {resp.ReasonPhrase}");

            var facts = await resp.Content.ReadFromJsonAsync<HostFacts>(JsonOptions, ct);
            if (facts is null) return DeviceResult<HostFacts>.Unreachable("bridge host returned an empty response");

            _cached = facts;
            return DeviceResult<HostFacts>.Success(facts);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return DeviceResult<HostFacts>.Unreachable($"bridge host error: {BridgeClient.Describe(ex)}");
        } catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) {
            return DeviceResult<HostFacts>.Unreachable($"bridge host error: {BridgeClient.Describe(ex)}");
        }
    }
}
