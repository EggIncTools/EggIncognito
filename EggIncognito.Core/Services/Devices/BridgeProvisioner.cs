using System.Net.Http.Json;
using System.Text.Json;

namespace EggIncognito.Core.Services.Devices;

public sealed class BridgeProvisioner(
    IHttpClientFactory httpFactory, DeviceTransportConfig cfg, VirtualDeviceConfig config) : IDeviceProvisioner {
    private const string LifecycleNote = "prod owns the container lifecycle";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Kind => config.Kind;

    public ProvisionerCapabilities Capabilities =>
        ProvisionerCapabilities.Create | ProvisionerCapabilities.Destroy | ProvisionerCapabilities.List;

    public async Task<DeviceResult<ProvisionedInstance>> CreateAsync(ProvisionSpec spec, CancellationToken ct) {
        string? image = string.IsNullOrWhiteSpace(spec.Image) ? null : spec.Image;
        var sent = await SendAsync<BridgeInstanceResult>(
            HttpMethod.Post, BridgeRoutes.Instances, new BridgeInstanceCreate(image), ct);
        if (sent.Failure is { } failure) return DeviceResult<ProvisionedInstance>.Unreachable(failure);
        if (sent.Body is not { Ok: true } body || body.Instance is not { } instance) {
            return DeviceResult<ProvisionedInstance>.Error(
                sent.Body?.Note ?? "the host did not create a virtual device");
        }

        return DeviceResult<ProvisionedInstance>.Success(Map(instance), body.Note);
    }

    public Task<DeviceResult> StartAsync(string instanceId, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(LifecycleNote));

    public Task<DeviceResult> StopAsync(string instanceId, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(LifecycleNote));

    public async Task<DeviceResult> DestroyAsync(string instanceId, CancellationToken ct) {
        var sent = await SendAsync<BridgeInstanceResult>(
            HttpMethod.Post, BridgeRoutes.InstanceDestroy(instanceId), null, ct);
        if (sent.Failure is { } failure) return DeviceResult.Unreachable(failure);
        return sent.Body is { Ok: true } body
            ? DeviceResult.Success(body.Note)
            : DeviceResult.Error(sent.Body?.Note ?? $"the host did not destroy '{instanceId}'");
    }

    public async Task<DeviceResult<IReadOnlyList<ProvisionedInstance>>> ListAsync(CancellationToken ct) {
        var sent = await SendAsync<BridgeInstanceList>(HttpMethod.Get, BridgeRoutes.Instances, null, ct);
        if (sent.Failure is { } failure)
            return DeviceResult<IReadOnlyList<ProvisionedInstance>>.Unreachable(failure);
        if (sent.Body is not { Ok: true } body) {
            return DeviceResult<IReadOnlyList<ProvisionedInstance>>.Error(
                sent.Body?.Note ?? "the host did not list its virtual devices");
        }

        var instances = body.Instances ?? [];
        return DeviceResult<IReadOnlyList<ProvisionedInstance>>.Success([.. instances.Select(Map)], body.Note);
    }

    private static ProvisionedInstance Map(BridgeInstance i) => new(
        i.InstanceId, i.Kind, i.Image, i.State, i.AdbSerial, i.HostRef, i.CreatedAt, i.Note, i.DeviceId);

    private async Task<(T? Body, string? Failure)> SendAsync<T>(
        HttpMethod method, string verb, object? payload, CancellationToken ct) where T : class {
        var client = new BridgeClient(httpFactory.CreateClient(BridgeClient.HttpClientName), cfg);
        if (client.ConfigurationNote is { } missing) return (null, missing);

        try {
            using var req = client.Build(method, verb);
            if (payload is not null) req.Content = JsonContent.Create(payload, payload.GetType(), null, JsonOptions);
            using var resp = await client.Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return (null, $"bridge {verb} {(int)resp.StatusCode} {resp.ReasonPhrase}");

            var body = await resp.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
            return body is null ? (null, $"bridge {verb} returned an empty response") : (body, null);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return (null, $"bridge {verb} error: {BridgeClient.Describe(ex)}");
        } catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) {
            return (null, $"bridge {verb} error: {BridgeClient.Describe(ex)}");
        }
    }
}
