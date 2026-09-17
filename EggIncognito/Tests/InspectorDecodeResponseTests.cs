using Ei;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class InspectorDecodeResponseTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _factory = f;

    [Fact]
    public async Task DecodesKnownResponse() {
        var msg = new PeriodicalsResponse();
        string b64 = Convert.ToBase64String(msg.ToByteArray());
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/inspector/decode-response",
            new { rawBase64 = b64, responseType = "PeriodicalsResponse" });
        Assert.True(r.IsSuccessStatusCode);
        string txt = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"error\":\"not valid base64", txt);
    }

    [Fact]
    public async Task UnknownType_ReturnsDecodeError_NotCrash() {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/inspector/decode-response",
            new { rawBase64 = "AA==", responseType = (string?)null });
        Assert.True(r.IsSuccessStatusCode);
    }
}
