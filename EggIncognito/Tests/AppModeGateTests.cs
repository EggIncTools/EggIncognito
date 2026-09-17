using System.Net;

namespace EggIncognito.Tests;

[Collection(HostedAppCollection.Name)]
public class AppModeGateTests(HostedAppFactory f) {
    [Fact]
    public async Task Hosted_CaptureStart_Is403() {
        var c = f.CreateClient();
        var r = await c.PostAsync("/api/capture/start", null);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Theory]
    [InlineData("/api/capture/stream")]
    [InlineData("/api/capture/flows")]
    [InlineData("/api/capture/stats")]
    [InlineData("/api/capture/decode?path=ei/first_contact&responseB64=AA==")]
    public async Task Hosted_CaptureReads_Are403(string path) {
        var c = f.CreateClient();
        var r = await c.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Hosted_ToolsDecode_Works() {
        var c = f.CreateClient();
        var r = await c.PostAsJsonAsync("/api/tools/decode", new { base64 = "" });
        Assert.True(r.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Mode_ReportsHosted() {
        var c = f.CreateClient();
        string json = await c.GetStringAsync("/api/app/mode");
        Assert.Contains("Hosted", json);
        Assert.Contains("\"canWrite\":false", json);
    }
}
