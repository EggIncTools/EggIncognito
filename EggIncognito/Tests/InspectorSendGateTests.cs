using System.Net;

namespace EggIncognito.Tests;

[Collection(HostedAppCollection.Name)]
public class InspectorSendGateTests(HostedAppFactory f) {
    [Fact]
    public async Task Hosted_Anonymous_Send_Is403() {
        var c = f.CreateClient();
        var r = await c.PostAsJsonAsync("/api/inspector/send",
            new { url = "https://www.auxbrain.com/ei/x", formBody = "data=AA==", responseType = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
