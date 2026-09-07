namespace EggIncognito.Core.Services.Devices;

public sealed class BridgeClient(HttpClient http, DeviceTransportConfig cfg) {
    public const string HttpClientName = "device-bridge";

    public HttpClient Http => http;

    public string? BaseUrl => cfg.RemoteBaseUrl;

    public string? ConfigurationNote {
        get {
            if (string.IsNullOrWhiteSpace(cfg.RemoteBaseUrl)) return "DeviceTransport:RemoteBaseUrl is not set";
            return string.IsNullOrWhiteSpace(cfg.ApiKey) ? "DeviceTransport:ApiKey is not set" : null;
        }
    }

    public HttpRequestMessage Build(HttpMethod method, string verb, string? query = null) {
        string url = BridgeRoutes.Absolute(cfg.RemoteBaseUrl, verb);
        if (!string.IsNullOrWhiteSpace(query)) url += "?" + query.TrimStart('?');
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(cfg.ApiKey)) req.Headers.Add(BridgeRoutes.SecretHeader, cfg.ApiKey);
        return req;
    }

    public static string Describe(Exception ex) {
        var parts = new List<string>();
        for (var e = ex; e is not null && parts.Count < 4; e = e.InnerException) {
            if (!string.IsNullOrWhiteSpace(e.Message)) parts.Add(e.Message);
        }

        return string.Join(" -> ", parts);
    }
}
