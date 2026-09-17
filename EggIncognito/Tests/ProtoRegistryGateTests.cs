using System.Net;

namespace EggIncognito.Tests;

[Collection(HostedAppCollection.Name)]
public class ProtoRegistryGateTests(HostedAppFactory f) {
    [Fact]
    public async Task StagedOffer_Anonymous_Is401() {
        var c = f.CreateClient();
        var r = await c.PostAsJsonAsync("/api/protos/staged/offer", new { protoSha = "x", protoText = "y" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
