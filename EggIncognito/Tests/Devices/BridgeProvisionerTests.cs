using System.Net;
using System.Text;
using System.Text.Json;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class BridgeProvisionerTests {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly BridgeInstance Instance = new(
        "egi-vd-abcd", "redroid", "redroid/redroid:11.0.0", ProvisionStates.Booting,
        "10.0.0.5:5555", "container-id", DateTimeOffset.UnixEpoch, "attached", "egi-vd-abcd");

    private static BridgeProvisioner Provisioner(HttpMessageHandler handler) => new(
        new StubHttpFactory(handler),
        new DeviceTransportConfig {
            Mode = DeviceTransportMode.Remote, RemoteBaseUrl = "https://host.test", ApiKey = "secret"
        },
        new VirtualDeviceConfig { Kind = "redroid" });

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) {
        Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task CreateAsync_PostsTheImageAndMapsTheInstance() {
        var handler = new RecordingHandler(_ => Json(new BridgeInstanceResult(true, "ok", "created", Instance)));
        var provisioner = Provisioner(handler);

        var created = await provisioner.CreateAsync(
            new ProvisionSpec("redroid", "redroid/redroid:11.0.0"), CancellationToken.None);

        Assert.True(created.Ok);
        Assert.Equal("https://host.test/api/bridge/instances", handler.Url);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("secret", handler.Secret);
        Assert.Contains("redroid/redroid:11.0.0", handler.Body ?? "", StringComparison.Ordinal);
        Assert.Equal("egi-vd-abcd", created.Value?.InstanceId);
        Assert.Equal("egi-vd-abcd", created.Value?.DeviceId);
        Assert.Equal("10.0.0.5:5555", created.Value?.AdbSerial);
    }

    [Fact]
    public async Task ListAsync_MapsEveryInstance() {
        var handler = new RecordingHandler(_ => Json(new BridgeInstanceList(true, "ok", null, [Instance])));

        var listed = await Provisioner(handler).ListAsync(CancellationToken.None);

        Assert.True(listed.Ok);
        Assert.Equal("https://host.test/api/bridge/instances", handler.Url);
        var only = Assert.Single(listed.Value ?? []);
        Assert.Equal("egi-vd-abcd", only.InstanceId);
        Assert.Equal(ProvisionStates.Booting, only.State);
        Assert.Equal("container-id", only.HostRef);
    }

    [Fact]
    public async Task DestroyAsync_PostsToTheInstanceVerb() {
        var handler = new RecordingHandler(_ => Json(new BridgeInstanceResult(true, "ok", "destroyed", null)));

        var destroyed = await Provisioner(handler).DestroyAsync("egi-vd-abcd", CancellationToken.None);

        Assert.True(destroyed.Ok);
        Assert.Equal("https://host.test/api/bridge/instances/egi-vd-abcd/destroy", handler.Url);
    }

    [Fact]
    public async Task ListAsync_UnreachableHost_FailsWithTheStatus() {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var listed = await Provisioner(handler).ListAsync(CancellationToken.None);

        Assert.False(listed.Ok);
        Assert.Equal(DeviceOutcome.Unreachable, listed.Outcome);
        Assert.Contains("502", listed.Note ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_IsUnsupportedBecauseTheHostOwnsTheLifecycle() {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var started = await Provisioner(handler).StartAsync("egi-vd-abcd", CancellationToken.None);

        Assert.Equal(DeviceOutcome.Unsupported, started.Outcome);
        Assert.Null(handler.Url);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
        public string? Url { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Secret { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            Url = request.RequestUri?.ToString();
            Method = request.Method;
            Secret = request.Headers.TryGetValues(BridgeRoutes.SecretHeader, out var values)
                ? values.FirstOrDefault()
                : null;
            if (request.Content is { } content) Body = await content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}
