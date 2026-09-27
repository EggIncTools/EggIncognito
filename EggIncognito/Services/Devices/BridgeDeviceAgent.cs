using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class BridgeDeviceAgent(
    IHttpClientFactory httpFactory, DeviceTransportConfig cfg, ILogger<BridgeDeviceAgent> logger) : IDeviceAgentClient {
    public bool Enabled => !string.IsNullOrWhiteSpace(cfg.RemoteBaseUrl) && !string.IsNullOrWhiteSpace(cfg.ApiKey);

    public Task<DeviceProbeDto?> ProbeAsync(string id, CancellationToken ct) => Task.FromResult<DeviceProbeDto?>(null);

    public Task<int> ProbeAllAsync(CancellationToken ct) => Task.FromResult(0);

    public async Task<bool> PokeAsync(string? id, bool force, CancellationToken ct) {
        var client = new BridgeClient(httpFactory.CreateClient(BridgeClient.HttpClientName), cfg);
        if (client.ConfigurationNote is { } missing) {
            logger.LogDebug("bridge poke skipped: {Note}", missing);
            return false;
        }

        try {
            using var req = client.Build(HttpMethod.Post, BridgeRoutes.PokeFor(id), force ? "force=true" : null);
            using var resp = await client.Http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning("bridge poke failed: {Note}", BridgeClient.Describe(ex));
            return false;
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return false;
        }
    }
}
