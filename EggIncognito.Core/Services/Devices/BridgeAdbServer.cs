namespace EggIncognito.Core.Services.Devices;

public sealed class BridgeAdbServer(IHttpClientFactory httpFactory, DeviceTransportConfig cfg) : IAdbServer {
    public string Socket => "remote";

    public bool Owned => false;

    public string Describe() => $"adb server lives on the bridge host at {cfg.RemoteBaseUrl}";

    public async Task RestartAsync(CancellationToken ct) {
        var client = new BridgeClient(httpFactory.CreateClient(BridgeClient.HttpClientName), cfg);
        if (client.ConfigurationNote is not null) return;

        try {
            using var req = client.Build(HttpMethod.Post, BridgeRoutes.AdbRestart);
            (await client.Http.SendAsync(req, ct)).Dispose();
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return;
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return;
        }
    }
}
