using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http.Features;

namespace EggIncognito.Services.Devices;

public sealed class DockerSocketProxy : IDisposable {
    private const int BufferSize = 64 * 1024;
    private readonly SocketsHttpHandler _handler;
    private readonly HttpClient _http;

    public DockerSocketProxy(string socketPath) {
        SocketPath = socketPath;
        _handler = new SocketsHttpHandler {
            ConnectCallback = async (_, ct) => {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct);
                    return new NetworkStream(socket, true);
                } catch {
                    socket.Dispose();
                    throw;
                }
            }
        };
        _http = new HttpClient(_handler, false) {
            BaseAddress = new Uri("http://docker/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public string SocketPath { get; }

    public bool Available => !OperatingSystem.IsWindows() && File.Exists(SocketPath);

    public async Task ForwardAsync(HttpContext http, string path, CancellationToken ct) {
        using var request = BuildRequest(http, path);
        try {
            using var upstream = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            http.Response.StatusCode = (int)upstream.StatusCode;
            if (upstream.Content.Headers.ContentType is { } contentType)
                http.Response.ContentType = contentType.ToString();
            http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            await using var body = await upstream.Content.ReadAsStreamAsync(ct);
            await PumpAsync(body, http.Response.Body, ct);
        } catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException) {
            if (http.Response.HasStarted) return;
            http.Response.Clear();
            http.Response.StatusCode = 502;
            await http.Response.WriteAsJsonAsync(new { error = ex.Message }, ct);
        }
    }

    private static HttpRequestMessage BuildRequest(HttpContext http, string path) {
        string relative = path.TrimStart('/') + http.Request.QueryString.Value;
        var request = new HttpRequestMessage(new HttpMethod(http.Request.Method), relative);
        if (!HasBody(http.Request)) return request;

        var content = new StreamContent(http.Request.Body);
        if (http.Request.ContentType is { Length: > 0 } contentType
            && MediaTypeHeaderValue.TryParse(contentType, out var parsed)) {
            content.Headers.ContentType = parsed;
        }

        if (http.Request.ContentLength is { } length) content.Headers.ContentLength = length;
        request.Content = content;
        return request;
    }

    private static bool HasBody(HttpRequest request) =>
        request.ContentLength is > 0 || string.Equals(request.Headers.TransferEncoding.ToString(), "chunked",
            StringComparison.OrdinalIgnoreCase);

    private static async Task PumpAsync(Stream from, Stream to, CancellationToken ct) {
        byte[] buffer = new byte[BufferSize];
        int read;
        while ((read = await from.ReadAsync(buffer, ct)) > 0) {
            await to.WriteAsync(buffer.AsMemory(0, read), ct);
            await to.FlushAsync(ct);
        }
    }

    public void Dispose() {
        _http.Dispose();
        _handler.Dispose();
    }
}
