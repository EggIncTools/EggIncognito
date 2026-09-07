using System.Net;
using System.Text;
using System.Text.Json;
using EggIncognito.Core.Services.Devices;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace EggIncognito.Tests.Devices;

public class BridgeProcessRunnerTests : IDisposable {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly List<string> _paths = [];

    private static DeviceTransportConfig Transport() => new() {
        Mode = DeviceTransportMode.Remote,
        RemoteBaseUrl = "https://host.test",
        ApiKey = "secret"
    };

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private string NewFile(byte[] bytes) {
        string path = DeviceShell.NewTempPath(".bin");
        File.WriteAllBytes(path, bytes);
        _paths.Add(path);
        return path;
    }

    private string NewMissingPath() {
        string path = DeviceShell.NewTempPath(".out");
        _paths.Add(path);
        return path;
    }

    [Fact]
    public async Task RunAsync_ExistingFileArg_BecomesInputPartAndPlaceholder() {
        byte[] bytes = [1, 2, 3, 4];
        string local = NewFile(bytes);
        var handler = new BridgeHandler(_ => Ok("""{"exit":0,"stdout":"pushed","stderr":"","outputs":[]}"""));
        var runner = new BridgeProcessRunner(new StubHttpFactory(handler), Transport());

        var result = await runner.RunAsync("adb", ["push", local, "/data/local/tmp/x"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("pushed", result.Stdout);
        var spec = handler.Spec();
        Assert.Equal("adb", spec.Exe);
        Assert.Equal(["push", BridgePlaceholders.In("1"), "/data/local/tmp/x"], spec.Args);
        Assert.Equal(bytes, handler.Files["1"]);
        Assert.Equal("https://host.test/api/bridge/exec", handler.Url);
        Assert.Equal("secret", handler.Secret);
    }

    [Fact]
    public async Task RunAsync_MissingTempPathArg_BecomesOutputAndIsWrittenBack() {
        string local = NewMissingPath();
        byte[] payload = "hello bridge"u8.ToArray();
        string body = JsonSerializer.Serialize(
            new BridgeExecResult(0, "", null, "", [new BridgeExecOutput("2", Convert.ToBase64String(payload))]),
            JsonOptions);
        var handler = new BridgeHandler(_ => Ok(body));
        var runner = new BridgeProcessRunner(new StubHttpFactory(handler), Transport());

        var result = await runner.RunAsync("adb", ["pull", "/sdcard/x", local], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var spec = handler.Spec();
        Assert.Equal(["pull", "/sdcard/x", BridgePlaceholders.Out("2")], spec.Args);
        Assert.Equal(["2"], spec.Outputs);
        Assert.True(File.Exists(local));
        Assert.Equal(payload, await File.ReadAllBytesAsync(local, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_ForeignArgs_PassThroughUnchanged() {
        var handler = new BridgeHandler(_ => Ok("""{"exit":0,"stdout":"","stderr":"","outputs":[]}"""));
        var runner = new BridgeProcessRunner(new StubHttpFactory(handler), Transport());

        await runner.RunAsync("adb", ["-s", "emulator:5555", "shell", "id"], CancellationToken.None);

        Assert.Equal(["-s", "emulator:5555", "shell", "id"], handler.Spec().Args);
        Assert.Empty(handler.Files);
    }

    [Fact]
    public async Task RunAsync_HttpFailure_MapsToExitMinusOneWithStatus() {
        var handler = new BridgeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var runner = new BridgeProcessRunner(new StubHttpFactory(handler), Transport());

        var result = await runner.RunAsync("adb", ["devices"], CancellationToken.None);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("403", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_MissingRemoteBaseUrl_ShortCircuitsWithoutARequest() {
        var handler = new BridgeHandler(_ => Ok("""{"exit":0,"stdout":"","stderr":"","outputs":[]}"""));
        var config = new DeviceTransportConfig { Mode = DeviceTransportMode.Remote, ApiKey = "secret" };
        var runner = new BridgeProcessRunner(new StubHttpFactory(handler), config);

        var result = await runner.RunAsync("adb", ["devices"], CancellationToken.None);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("RemoteBaseUrl", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task RunBytesAsync_DecodesBase64Stdout() {
        byte[] payload = [9, 8, 7];
        string body = JsonSerializer.Serialize(
            new BridgeExecResult(0, null, Convert.ToBase64String(payload), "", []), JsonOptions);
        var handler = new BridgeHandler(_ => Ok(body));
        var runner = new BridgeProcessRunner(new StubHttpFactory(handler), Transport());

        var result = await runner.RunBytesAsync("adb", ["exec-out", "screencap"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(payload, result.Stdout);
        Assert.True(handler.Spec().BinaryStdout);
    }

    public void Dispose() {
        foreach (string path in _paths) DeviceShell.TryDelete(path);
        GC.SuppressFinalize(this);
    }

    private sealed class BridgeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
        public int Calls { get; private set; }
        public string? Url { get; private set; }
        public string? Secret { get; private set; }
        public string? SpecJson { get; private set; }
        public Dictionary<string, byte[]> Files { get; } = [];

        public BridgeExecSpec Spec() =>
            JsonSerializer.Deserialize<BridgeExecSpec>(SpecJson ?? "", JsonOptions)
            ?? throw new InvalidOperationException("no spec part was sent");

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            Calls++;
            Url = request.RequestUri?.ToString();
            Secret = request.Headers.TryGetValues(BridgeRoutes.SecretHeader, out var values)
                ? values.FirstOrDefault()
                : null;
            if (request.Content is { } content) await ReadPartsAsync(content, cancellationToken);
            return respond(request);
        }

        private async Task ReadPartsAsync(HttpContent content, CancellationToken ct) {
            string? raw = content.Headers.ContentType?.ToString();
            if (raw is null) return;

            string boundary = HeaderUtilities.RemoveQuotes(MediaTypeHeaderValue.Parse(raw).Boundary).Value ?? "";
            var reader = new MultipartReader(boundary, await content.ReadAsStreamAsync(ct));
            for (var section = await reader.ReadNextSectionAsync(ct);
                 section is not null;
                 section = await reader.ReadNextSectionAsync(ct)) {
                string? name = section.ContentDisposition is { } disposition
                    ? HeaderUtilities.RemoveQuotes(ContentDispositionHeaderValue.Parse(disposition).Name).Value
                    : null;
                if (name is null) continue;

                if (string.Equals(name, BridgeExecParts.Spec, StringComparison.Ordinal)) {
                    using var text = new StreamReader(section.Body, Encoding.UTF8);
                    SpecJson = await text.ReadToEndAsync(ct);
                    continue;
                }

                using var buffer = new MemoryStream();
                await section.Body.CopyToAsync(buffer, ct);
                Files[name] = buffer.ToArray();
            }
        }
    }
}
