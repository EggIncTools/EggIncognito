using System.Net;
using System.Text;
using System.Text.Json;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Devices;

public class BridgeDeviceFleetTests {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static BridgeDeviceFleet Fleet(HttpMessageHandler handler) {
        var source = new BridgeDeviceFleetSource(
            new StubHttpFactory(handler),
            new DeviceTransportConfig {
                Mode = DeviceTransportMode.Remote,
                RemoteBaseUrl = "https://host.test",
                ApiKey = "secret"
            },
            TimeProvider.System);
        return new BridgeDeviceFleet(source, NullLogger<BridgeDeviceFleet>.Instance);
    }

    private static HttpResponseMessage Json(BridgeFleet fleet) => new(HttpStatusCode.OK) {
        Content = new StringContent(JsonSerializer.Serialize(fleet, JsonOptions), Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task EnabledAsync_MapsEveryEntry() {
        var payload = new BridgeFleet([
            new BridgeFleetEntry("egi-vd-1", "android", "egi-vd-1", "10.0.0.5:5555", "com.auxbrain.egginc",
                "virtual", 8081)
        ], "192.168.1.66");
        var fleet = Fleet(new StubHandler(_ => Json(payload)));

        var devices = await fleet.EnabledAsync(CancellationToken.None);

        var only = Assert.Single(devices);
        Assert.Equal("egi-vd-1", only.Id);
        Assert.Equal("android", only.Platform);
        Assert.Equal("10.0.0.5:5555", only.Target);
        Assert.Equal("com.auxbrain.egginc", only.Package);
        Assert.Equal("virtual", only.Origin);
        Assert.Equal(8081, only.CapturePort);
        Assert.Equal("192.168.1.66", await fleet.CaptureHostIpAsync(CancellationToken.None));
        Assert.Equal(8081, await fleet.CapturePortAsync("egi-vd-1", CancellationToken.None));
    }

    [Fact]
    public async Task EnabledAsync_FailedRead_IsEmpty() {
        var fleet = Fleet(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        Assert.Empty(await fleet.EnabledAsync(CancellationToken.None));
        Assert.Null(await fleet.CaptureHostIpAsync(CancellationToken.None));
        Assert.Equal(0, await fleet.CapturePortAsync("egi-vd-1", CancellationToken.None));
    }

    [Fact]
    public async Task CapturePortAsync_UnknownDevice_IsZero() {
        var payload = new BridgeFleet([
            new BridgeFleetEntry("egi-vd-1", "android", "egi-vd-1", "10.0.0.5:5555", "com.auxbrain.egginc",
                "virtual", 8081)
        ], "192.168.1.66");
        var fleet = Fleet(new StubHandler(_ => Json(payload)));

        Assert.Equal(0, await fleet.CapturePortAsync("egi-vd-9", CancellationToken.None));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
