using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace EggIncognito.Core.Services.Devices;

public sealed class BridgeDeviceClaims(IHttpClientFactory httpFactory, DeviceTransportConfig cfg) : IDeviceClaims {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool Active => true;

    public async Task<DeviceResult<DateTimeOffset>> ClaimAsync(
        string deviceId, TimeSpan? ttl, CancellationToken ct) {
        var client = new BridgeClient(httpFactory.CreateClient(BridgeClient.HttpClientName), cfg);
        if (client.ConfigurationNote is { } missing) return DeviceResult<DateTimeOffset>.Unsupported(missing);

        int? seconds = ttl is { } span ? Math.Max(1, (int)Math.Round(span.TotalSeconds)) : null;
        try {
            using var req = client.Build(HttpMethod.Post, Verb(BridgeRoutes.Claim, deviceId));
            req.Content = JsonContent.Create(new BridgeClaimBody(seconds), options: JsonOptions);
            using var resp = await client.Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) {
                return DeviceResult<DateTimeOffset>.Error(
                    $"the host refused a claim on {deviceId} ({(int)resp.StatusCode} {resp.ReasonPhrase})");
            }

            var outcome = await resp.Content.ReadFromJsonAsync<BridgeClaimOutcome>(JsonOptions, ct);
            if (outcome is not { Ok: true })
                return DeviceResult<DateTimeOffset>.Error($"the host did not grant a claim on {deviceId}");

            return DeviceResult<DateTimeOffset>.Success(outcome.ExpiresAt,
                $"claimed until {outcome.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)}");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return DeviceResult<DateTimeOffset>.Unreachable($"bridge claim error: {BridgeClient.Describe(ex)}");
        } catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) {
            return DeviceResult<DateTimeOffset>.Unreachable($"bridge claim error: {BridgeClient.Describe(ex)}");
        }
    }

    public async Task ReleaseAsync(string deviceId, CancellationToken ct) =>
        await TryReleaseAsync(deviceId, ct);

    private async Task<bool> TryReleaseAsync(string deviceId, CancellationToken ct) {
        var client = new BridgeClient(httpFactory.CreateClient(BridgeClient.HttpClientName), cfg);
        if (client.ConfigurationNote is not null) return false;

        try {
            using var req = client.Build(HttpMethod.Post, Verb(BridgeRoutes.Release, deviceId));
            using var resp = await client.Http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return false;
        } catch (OperationCanceledException) {
            return false;
        }
    }

    private static string Verb(string route, string deviceId) => $"{route}/{Uri.EscapeDataString(deviceId)}";
}
