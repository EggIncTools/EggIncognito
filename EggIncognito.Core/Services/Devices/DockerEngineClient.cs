using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services.Devices;

public sealed record DockerContainer(
    string Id,
    string Name,
    string Image,
    string State,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> Labels,
    string? IpAddress = null);

public sealed record DockerInspect(
    string Id,
    string Name,
    string Image,
    bool Running,
    string Status,
    DateTimeOffset? StartedAt,
    IReadOnlyList<string> Networks,
    IReadOnlyDictionary<string, string> Labels,
    string? IpAddress = null);

public sealed record DockerCreateSpec(
    string Name,
    string Image,
    IReadOnlyList<string> Cmd,
    IReadOnlyList<string> Binds,
    string? Network,
    IReadOnlyDictionary<string, string> Labels,
    bool Privileged = true,
    string RestartPolicy = "unless-stopped");

public sealed record DockerImage(
    IReadOnlyList<string> RepoTags,
    string Id,
    long Size,
    DateTimeOffset Created);

public sealed record DockerEvent(string Type, string Action, string Id, string Name);

public sealed class DockerEventReader : IAsyncDisposable {
    private readonly HttpResponseMessage _response;
    private readonly Stream _body;
    private readonly StreamReader _reader;

    internal DockerEventReader(HttpResponseMessage response, Stream body) {
        _response = response;
        _body = body;
        _reader = new StreamReader(body, Encoding.UTF8);
    }

    public async Task<DockerEvent?> ReadAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            string? line = await _reader.ReadLineAsync(ct);
            if (line is null) return null;
            if (line.Length == 0) continue;
            if (DockerEngineClient.ParseEvent(line) is { } ev) return ev;
        }

        return null;
    }

    public async ValueTask DisposeAsync() {
        _reader.Dispose();
        await _body.DisposeAsync();
        _response.Dispose();
    }
}

public sealed partial class DockerEngineClient : IDisposable {
    public const string HostNetwork = "host";
    private static readonly string[] ReservedNetworks = ["bridge", "host", "none"];
    private static readonly Uri UnixBase = new("http://docker/");
    private readonly HttpClient _http;
    private readonly HttpClient _build;
    private readonly HttpClient _stream;
    private readonly SocketsHttpHandler _handler;

    public DockerEngineClient(DockerEndpoint endpoint) {
        Endpoint = endpoint;
        _handler = endpoint is DockerEndpoint.Unix unix ? UnixHandler(unix.SocketPath) : new SocketsHttpHandler();

        var baseAddress = UnixBase;
        if (endpoint is DockerEndpoint.Bridge bridge && Uri.TryCreate(bridge.Root, UriKind.Absolute, out var parsed))
            baseAddress = parsed;

        _http = NewClient(baseAddress, TimeSpan.FromSeconds(90));
        _build = NewClient(baseAddress, Timeout.InfiniteTimeSpan);
        _stream = NewClient(baseAddress, Timeout.InfiniteTimeSpan);

        if (endpoint is not DockerEndpoint.Bridge { Secret: { Length: > 0 } secret }) return;
        foreach (var client in new[] { _http, _build, _stream })
            client.DefaultRequestHeaders.Add(BridgeRoutes.SecretHeader, secret);
    }

    private static SocketsHttpHandler UnixHandler(string socketPath) => new() {
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

    private HttpClient NewClient(Uri baseAddress, TimeSpan timeout) {
        var client = new HttpClient(_handler, false) { BaseAddress = baseAddress, Timeout = timeout };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public DockerEndpoint Endpoint { get; }

    public bool Available => Endpoint.Available;

    private string NotAvailable => $"{Endpoint.Describe()} is not available";

    private string Unreachable(Exception ex) => $"{Endpoint.Describe()} unreachable: {ex.Message}";

    public void Dispose() {
        _http.Dispose();
        _build.Dispose();
        _stream.Dispose();
        _handler.Dispose();
    }

    public async Task<DeviceResult<DockerEventReader>> OpenEventsAsync(string? label, CancellationToken ct) {
        if (!Available) return DeviceResult<DockerEventReader>.Unsupported(NotAvailable);

        string filters = string.IsNullOrEmpty(label)
            ? "{\"type\":[\"container\"]}"
            : $"{{\"type\":[\"container\"],\"label\":[\"{label}\"]}}";
        using var req = new HttpRequestMessage(HttpMethod.Get, "events?filters=" + Uri.EscapeDataString(filters));

        HttpResponseMessage? res = null;
        Stream? body = null;
        try {
            res = await _stream.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode) {
                int code = (int)res.StatusCode;
                string text = await res.Content.ReadAsStringAsync(ct);
                return DeviceResult<DockerEventReader>.Error($"docker {code}: {Trim(text)}");
            }

            body = await res.Content.ReadAsStreamAsync(ct);
            var opened = new DockerEventReader(res, body);
            res = null;
            body = null;
            return DeviceResult<DockerEventReader>.Success(opened);
        } catch (HttpRequestException ex) {
            return DeviceResult<DockerEventReader>.Unsupported(Unreachable(ex));
        } catch (SocketException ex) {
            return DeviceResult<DockerEventReader>.Unsupported(Unreachable(ex));
        } catch (IOException ex) {
            return DeviceResult<DockerEventReader>.Unreachable($"docker event stream broke: {ex.Message}");
        } finally {
            if (body is not null) await body.DisposeAsync();
            res?.Dispose();
        }
    }

    internal static DockerEvent? ParseEvent(string line) {
        try {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string action = Str(root, "Action");
            if (action.Length == 0) action = Str(root, "status");
            if (action.Length == 0) return null;

            string id = Str(root, "id");
            string name = "";
            if (root.TryGetProperty("Actor", out var actor) && actor.ValueKind == JsonValueKind.Object) {
                if (id.Length == 0) id = Str(actor, "ID");
                if (actor.TryGetProperty("Attributes", out var attrs)) name = Str(attrs, "name");
            }

            return new DockerEvent(Str(root, "Type"), action, id, name);
        } catch (JsonException) {
            return null;
        }
    }

    public async Task<DeviceResult> PingAsync(CancellationToken ct) {
        if (!Available) return DeviceResult.Unsupported(NotAvailable);
        var res = await SendAsync(HttpMethod.Get, "_ping", null, ct);
        return res.Ok ? DeviceResult.Success() : new DeviceResult(res.Outcome, res.Note);
    }

    public async Task<DeviceResult<IReadOnlyList<DockerContainer>>> ListAsync(
        string? label, CancellationToken ct) {
        string path = "containers/json?all=1";
        if (!string.IsNullOrEmpty(label)) {
            string filters = $"{{\"label\":[\"{label}\"]}}";
            path += "&filters=" + Uri.EscapeDataString(filters);
        }

        var res = await SendAsync(HttpMethod.Get, path, null, ct);
        if (!res.Ok) return new DeviceResult<IReadOnlyList<DockerContainer>>(res.Outcome, null, res.Note);

        try {
            using var doc = JsonDocument.Parse(res.Value ?? "[]");
            var list = new List<DockerContainer>();
            foreach (var el in doc.RootElement.EnumerateArray()) list.Add(ReadSummary(el));
            return DeviceResult<IReadOnlyList<DockerContainer>>.Success(list);
        } catch (JsonException ex) {
            return DeviceResult<IReadOnlyList<DockerContainer>>.Error($"unreadable container list: {ex.Message}");
        }
    }

    public async Task<DeviceResult<DockerInspect>> InspectAsync(string idOrName, CancellationToken ct) {
        var res = await SendAsync(HttpMethod.Get, $"containers/{Uri.EscapeDataString(idOrName)}/json", null, ct);
        if (!res.Ok) return new DeviceResult<DockerInspect>(res.Outcome, null, res.Note);

        try {
            using var doc = JsonDocument.Parse(res.Value ?? "{}");
            return DeviceResult<DockerInspect>.Success(ReadInspect(doc.RootElement));
        } catch (JsonException ex) {
            return DeviceResult<DockerInspect>.Error($"unreadable inspect payload: {ex.Message}");
        }
    }

    public async Task<DeviceResult<string>> CreateAsync(DockerCreateSpec spec, CancellationToken ct) {
        var hostConfig = new Dictionary<string, object?> {
            ["Privileged"] = spec.Privileged,
            ["Binds"] = spec.Binds,
            ["RestartPolicy"] = new Dictionary<string, object?> { ["Name"] = spec.RestartPolicy }
        };
        if (!string.IsNullOrEmpty(spec.Network)) hostConfig["NetworkMode"] = spec.Network;

        var body = new Dictionary<string, object?> {
            ["Image"] = spec.Image,
            ["Cmd"] = spec.Cmd,
            ["Labels"] = spec.Labels,
            ["HostConfig"] = hostConfig
        };

        var res = await SendAsync(HttpMethod.Post,
            $"containers/create?name={Uri.EscapeDataString(spec.Name)}", JsonSerializer.Serialize(body), ct);
        if (!res.Ok) return new DeviceResult<string>(res.Outcome, null, res.Note);

        try {
            using var doc = JsonDocument.Parse(res.Value ?? "{}");
            string? id = doc.RootElement.TryGetProperty("Id", out var idEl) ? idEl.GetString() : null;
            return string.IsNullOrEmpty(id)
                ? DeviceResult<string>.Error("docker returned no container id")
                : DeviceResult<string>.Success(id);
        } catch (JsonException ex) {
            return DeviceResult<string>.Error($"unreadable create payload: {ex.Message}");
        }
    }

    public async Task<DeviceResult> StartAsync(string id, CancellationToken ct) =>
        Plain(await SendAsync(HttpMethod.Post, $"containers/{Uri.EscapeDataString(id)}/start", null, ct));

    public async Task<DeviceResult> StopAsync(string id, int timeoutSeconds, CancellationToken ct) {
        string t = timeoutSeconds.ToString(CultureInfo.InvariantCulture);
        return Plain(await SendAsync(HttpMethod.Post, $"containers/{Uri.EscapeDataString(id)}/stop?t={t}", null, ct));
    }

    public async Task<DeviceResult> RemoveAsync(string id, CancellationToken ct) =>
        Plain(await SendAsync(HttpMethod.Delete, $"containers/{Uri.EscapeDataString(id)}?force=1&v=1", null, ct, true));

    public async Task<DeviceResult> RemoveVolumeAsync(string name, CancellationToken ct) =>
        Plain(await SendAsync(HttpMethod.Delete, $"volumes/{Uri.EscapeDataString(name)}?force=1", null, ct, true));

    public async Task<DeviceResult<IReadOnlyList<DockerImage>>> ListImagesAsync(string? reference, CancellationToken ct) {
        string path = "images/json";
        if (!string.IsNullOrEmpty(reference)) {
            string filters = $"{{\"reference\":[\"{reference}\"]}}";
            path += "?filters=" + Uri.EscapeDataString(filters);
        }

        var res = await SendAsync(HttpMethod.Get, path, null, ct);
        if (!res.Ok) return new DeviceResult<IReadOnlyList<DockerImage>>(res.Outcome, null, res.Note);

        try {
            using var doc = JsonDocument.Parse(res.Value ?? "[]");
            var list = new List<DockerImage>();
            foreach (var el in doc.RootElement.EnumerateArray()) list.Add(ReadImage(el));
            return DeviceResult<IReadOnlyList<DockerImage>>.Success(list);
        } catch (JsonException ex) {
            return DeviceResult<IReadOnlyList<DockerImage>>.Error($"unreadable image list: {ex.Message}");
        }
    }

    public async Task<DeviceResult> RemoveImageAsync(string tagOrId, CancellationToken ct) =>
        Plain(await SendAsync(HttpMethod.Delete, $"images/{Uri.EscapeDataString(tagOrId)}?force=1", null, ct, true));

    public async Task<DeviceResult> BuildImageAsync(
        Stream tarContext, string tag, IReadOnlyDictionary<string, string>? buildArgs, Action<string> onLog,
        CancellationToken ct) {
        if (!Available) return DeviceResult.Unsupported(NotAvailable);

        string path = $"build?t={Uri.EscapeDataString(tag)}&rm=1&forcerm=1";
        if (buildArgs is { Count: > 0 })
            path += "&buildargs=" + Uri.EscapeDataString(JsonSerializer.Serialize(buildArgs));

        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Content = new StreamContent(tarContext);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-tar");

        try {
            using var res = await _build.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            string? error = null;
            await using (var body = await res.Content.ReadAsStreamAsync(ct)) {
                using var reader = new StreamReader(body, Encoding.UTF8);
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) is not null) {
                    if (line.Length == 0) continue;
                    (string? text, string? err) = ParseBuildLine(line);
                    if (text is { Length: > 0 }) onLog(text);
                    if (err is { Length: > 0 }) error = err;
                }
            }

            if (!res.IsSuccessStatusCode) return DeviceResult.Error($"docker build {(int)res.StatusCode}: {error ?? "no detail"}");
            return error is null ? DeviceResult.Success() : DeviceResult.Error(error);
        } catch (HttpRequestException ex) {
            return DeviceResult.Unsupported(Unreachable(ex));
        } catch (SocketException ex) {
            return DeviceResult.Unsupported(Unreachable(ex));
        } catch (IOException ex) {
            return DeviceResult.Unreachable($"docker build stream broke: {ex.Message}");
        }
    }

    private static (string? Text, string? Error) ParseBuildLine(string line) {
        try {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);

            if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String)
                return (null, errEl.GetString());
            if (root.TryGetProperty("errorDetail", out var detail)
                && detail.ValueKind == JsonValueKind.Object
                && detail.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                return (null, msg.GetString());

            if (root.TryGetProperty("stream", out var s) && s.ValueKind == JsonValueKind.String)
                return (s.GetString()?.TrimEnd('\r', '\n'), null);
            if (root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String) {
                string? id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                string status = st.GetString() ?? "";
                return (id is { Length: > 0 } ? $"{id}: {status}" : status, null);
            }

            return (null, null);
        } catch (JsonException) {
            return (line, null);
        }
    }

    private static DockerImage ReadImage(JsonElement el) {
        var tags = new List<string>();
        if (el.TryGetProperty("RepoTags", out var rt) && rt.ValueKind == JsonValueKind.Array) {
            foreach (var t in rt.EnumerateArray()) {
                string? tag = t.GetString();
                if (tag is { Length: > 0 } and not "<none>:<none>") tags.Add(tag);
            }
        }

        long size = el.TryGetProperty("Size", out var sz) && sz.TryGetInt64(out long sv) ? sv : 0;
        long created = el.TryGetProperty("Created", out var c) && c.TryGetInt64(out long cv) ? cv : 0;
        return new DockerImage(tags, Str(el, "Id"), size, DateTimeOffset.FromUnixTimeSeconds(created));
    }

    public async Task<DeviceResult<string>> SelfNetworkAsync(CancellationToken ct) {
        if (Endpoint is DockerEndpoint.Bridge)
            return DeviceResult<string>.Unsupported("the app network on a bridge host is a host fact");
        if (!Available) return DeviceResult<string>.Unsupported(NotAvailable);

        var tried = new List<string>();
        foreach (string candidate in SelfIdCandidates()) {
            if (string.IsNullOrWhiteSpace(candidate) || tried.Contains(candidate, StringComparer.Ordinal)) continue;
            tried.Add(candidate);
            var inspect = await InspectAsync(candidate, ct);
            if (!inspect.Ok || inspect.Value is not { } self) continue;

            string? network = self.Networks
                .FirstOrDefault(n => !ReservedNetworks.Contains(n, StringComparer.OrdinalIgnoreCase));
            if (network is not null) return DeviceResult<string>.Success(network);

            if (self.Networks.Contains(HostNetwork, StringComparer.OrdinalIgnoreCase))
                return DeviceResult<string>.Success(HostNetwork);

            return DeviceResult<string>.Error(
                $"this container is only on {string.Join(", ", self.Networks)}; attach it to a user-defined docker " +
                "network or run it with network_mode: host");
        }

        return DeviceResult<string>.Error(
            "could not identify this container from the docker daemon " +
            $"(tried {(tried.Count == 0 ? "no candidates" : string.Join(", ", tried))}); " +
            "the app must run as a container for virtual devices to work");
    }

    private static IEnumerable<string> SelfIdCandidates() {
        yield return Environment.MachineName;
        foreach (string id in ReadProcIds("/proc/self/mountinfo")) yield return id;
        foreach (string id in ReadProcIds("/proc/self/cgroup")) yield return id;
    }

    private static List<string> ReadProcIds(string path) {
        string text;
        try {
            if (!File.Exists(path)) return [];
            text = File.ReadAllText(path);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return [];
        }

        return [.. ContainerId().Matches(text).Select(m => m.Value).Distinct(StringComparer.Ordinal)];
    }

    [GeneratedRegex("[0-9a-f]{64}")]
    private static partial Regex ContainerId();

    private static DeviceResult Plain(DeviceResult<string> res) =>
        res.Ok ? DeviceResult.Success(res.Note) : new DeviceResult(res.Outcome, res.Note);

    private static DockerContainer ReadSummary(JsonElement el) {
        string name = el.TryGetProperty("Names", out var names) && names.ValueKind == JsonValueKind.Array
            ? names.EnumerateArray().Select(n => n.GetString() ?? "").FirstOrDefault()?.TrimStart('/') ?? ""
            : "";
        long created = el.TryGetProperty("Created", out var c) && c.TryGetInt64(out long v) ? v : 0;
        return new DockerContainer(
            Str(el, "Id"),
            name,
            Str(el, "Image"),
            Str(el, "State"),
            Str(el, "Status"),
            DateTimeOffset.FromUnixTimeSeconds(created),
            Labels(el.TryGetProperty("Labels", out var lb) ? lb : default),
            FirstIp(el));
    }

    private static string? FirstIp(JsonElement root) {
        if (!root.TryGetProperty("NetworkSettings", out var ns)
            || !ns.TryGetProperty("Networks", out var nets)
            || nets.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var n in nets.EnumerateObject()) {
            string ip = Str(n.Value, "IPAddress");
            if (ip.Length > 0) return ip;
        }

        return null;
    }

    private static DockerInspect ReadInspect(JsonElement root) {
        var state = root.TryGetProperty("State", out var s) ? s : default;
        var config = root.TryGetProperty("Config", out var cfg) ? cfg : default;
        bool running = state.ValueKind == JsonValueKind.Object
                       && state.TryGetProperty("Running", out var r)
                       && r.ValueKind == JsonValueKind.True;

        DateTimeOffset? startedAt = null;
        if (state.ValueKind == JsonValueKind.Object
            && state.TryGetProperty("StartedAt", out var sa)
            && DateTimeOffset.TryParse(sa.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var parsed)
            && parsed.Year > 1)
            startedAt = parsed;

        var networks = new List<string>();
        if (root.TryGetProperty("NetworkSettings", out var ns)
            && ns.TryGetProperty("Networks", out var nets)
            && nets.ValueKind == JsonValueKind.Object) {
            foreach (var n in nets.EnumerateObject()) networks.Add(n.Name);
        }

        return new DockerInspect(
            Str(root, "Id"),
            Str(root, "Name").TrimStart('/'),
            config.ValueKind == JsonValueKind.Object ? Str(config, "Image") : "",
            running,
            state.ValueKind == JsonValueKind.Object ? Str(state, "Status") : "",
            startedAt,
            networks,
            Labels(config.ValueKind == JsonValueKind.Object && config.TryGetProperty("Labels", out var lb)
                ? lb
                : default),
            FirstIp(root));
    }

    private static string Str(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static Dictionary<string, string> Labels(JsonElement el) {
        if (el.ValueKind != JsonValueKind.Object) return [with(StringComparer.Ordinal)];
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in el.EnumerateObject()) map[p.Name] = p.Value.GetString() ?? "";
        return map;
    }

    private async Task<DeviceResult<string>> SendAsync(
        HttpMethod method, string path, string? json, CancellationToken ct, bool allowMissing = false) {
        if (!Available) return DeviceResult<string>.Unsupported(NotAvailable);

        using var req = new HttpRequestMessage(method, path);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try {
            using var res = await _http.SendAsync(req, ct);
            string body = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode) return DeviceResult<string>.Success(body);
            if (res.StatusCode == HttpStatusCode.NotFound && allowMissing)
                return DeviceResult<string>.Success("", "already gone");
            return DeviceResult<string>.Error($"docker {(int)res.StatusCode}: {Trim(body)}");
        } catch (HttpRequestException ex) {
            return DeviceResult<string>.Unsupported(Unreachable(ex));
        } catch (SocketException ex) {
            return DeviceResult<string>.Unsupported(Unreachable(ex));
        } catch (IOException ex) {
            return DeviceResult<string>.Unsupported(Unreachable(ex));
        } catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) {
            return DeviceResult<string>.Unreachable($"docker request timed out: {ex.Message}");
        }
    }

    private static string Trim(string body) {
        string one = body.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return one.Length <= 300 ? one : one[..300];
    }
}
