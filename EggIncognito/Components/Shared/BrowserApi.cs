using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EggIncognito.Components.Shared;

public sealed record BrowserResponse(
    int Status,
    bool Ok,
    string ContentType,
    string Body,
    bool Binary,
    long ByteSize,
    string? FileName) {
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static BrowserResponse Failed(string why) => new(0, false, "", why, false, 0, null);

    public T? Json<T>() {
        if (Binary || string.IsNullOrWhiteSpace(Body)) return default;
        try {
            return JsonSerializer.Deserialize<T>(Body, Options);
        } catch (JsonException) {
            return default;
        }
    }

    public byte[] Bytes() => Binary ? Convert.FromBase64String(Body) : Encoding.UTF8.GetBytes(Body);

    public string Describe() {
        if (Json<ErrorBody>()?.Error is { Length: > 0 } detail) return detail;
        if (Status > 0) return $"HTTP {Status}";
        return Body.Length > 0 ? Body : "request failed";
    }

    private sealed record ErrorBody(string? Error);
}

public sealed class BrowserApi(Func<HttpClient> client) {
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public Task<BrowserResponse> GetAsync(string url) => SendAsync("GET", url);

    public Task<BrowserResponse> PostAsync(string url, TimeSpan? timeout = null) => SendAsync("POST", url, timeout: timeout);

    public Task<BrowserResponse> DeleteAsync(string url) => SendAsync("DELETE", url);

    public Task<BrowserResponse> PostJsonAsync<T>(string url, T body, TimeSpan? timeout = null) =>
        SendAsync("POST", url, JsonSerializer.Serialize(body, Options), timeout: timeout);

    public Task<BrowserResponse> PutJsonAsync<T>(string url, T body) => SendAsync("PUT", url, JsonSerializer.Serialize(body, Options));

    public Task<BrowserResponse> SendAsync(string method, string url, string? body = null,
        string contentType = "application/json", TimeSpan? timeout = null) {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, contentType);
        return ExecuteAsync(request, timeout);
    }

    public Task<BrowserResponse> SendFileAsync(string method, string url, string field, string fileName, byte[] bytes) {
        var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), field, fileName } };
        return ExecuteAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = form }, null);
    }

    private async Task<BrowserResponse> ExecuteAsync(HttpRequestMessage request, TimeSpan? timeout) {
        using var http = client();
        if (timeout is { } t) http.Timeout = t;
        try {
            using var response = await http.SendAsync(request);
            return await DescribeAsync(response);
        } catch (TaskCanceledException) {
            return BrowserResponse.Failed("request timed out");
        } catch (HttpRequestException ex) {
            return BrowserResponse.Failed(ex.Message);
        } finally {
            request.Dispose();
        }
    }

    private static async Task<BrowserResponse> DescribeAsync(HttpResponseMessage response) {
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "";
        var fileName = AttachmentName(response.Content.Headers.ContentDisposition);
        var status = (int)response.StatusCode;
        if (Textual(contentType)) {
            var text = await response.Content.ReadAsStringAsync();
            return new BrowserResponse(status, response.IsSuccessStatusCode, contentType, text, false, text.Length, fileName);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        return new BrowserResponse(status, response.IsSuccessStatusCode, contentType, Convert.ToBase64String(bytes), true,
            bytes.LongLength, fileName);
    }

    private static string? AttachmentName(ContentDispositionHeaderValue? disposition) =>
        (disposition?.FileNameStar ?? disposition?.FileName)?.Trim('"');

    private static bool Textual(string contentType) =>
        contentType.Length == 0 || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                                || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
                                || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
}
