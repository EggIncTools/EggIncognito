using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EggIncognito.Services.EggSharpSender;

public sealed record EggSharpAppConfig(string AppId, string InstallationId, string PrivateKeyPem) {
    public const string Owner = "EggIncTools";
    public const string Repo = "EggSharp";

    public static EggSharpAppConfig? FromEnvironment() {
        string? appId = Environment.GetEnvironmentVariable("EGGSHARP_APP_ID");
        string? installation = Environment.GetEnvironmentVariable("EGGSHARP_APP_INSTALLATION_ID");
        string? key = Environment.GetEnvironmentVariable("EGGSHARP_APP_PRIVATE_KEY");
        return string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(installation) || string.IsNullOrWhiteSpace(key)
            ? null
            : new EggSharpAppConfig(appId, installation, key.Replace("\\n", "\n", StringComparison.Ordinal));
    }
}

public sealed class EggSharpGitHub(HttpClient http, EggSharpAppConfig config, TimeProvider time) {
    private const string Api = "https://api.github.com";
    private string? _token;
    private DateTimeOffset _tokenExpires;

    private static string RepoPath => $"{Api}/repos/{EggSharpAppConfig.Owner}/{EggSharpAppConfig.Repo}";

    public async Task<string> MainShaAsync(CancellationToken ct) =>
        (await GetAsync($"{RepoPath}/git/ref/heads/main", ct))!["object"]!["sha"]!.GetValue<string>();

    public async Task<string?> FileTextAsync(string path, string reference, CancellationToken ct) {
        var node = await GetAsync($"{RepoPath}/contents/{path}?ref={Uri.EscapeDataString(reference)}", ct, allowMissing: true);
        if (node?["content"]?.GetValue<string>() is not { } b64) return null;
        return Encoding.UTF8.GetString(Convert.FromBase64String(b64.Replace("\n", "", StringComparison.Ordinal)));
    }

    public async Task<bool> OpenPullExistsAsync(string branch, CancellationToken ct) {
        var node = await GetAsync($"{RepoPath}/pulls?state=open&head={EggSharpAppConfig.Owner}:{Uri.EscapeDataString(branch)}", ct);
        return node is JsonArray { Count: > 0 };
    }

    public async Task<string> CommitFilesAsync(string parentSha, IReadOnlyDictionary<string, string> files, string message,
        CancellationToken ct) {
        var parent = await GetAsync($"{RepoPath}/git/commits/{parentSha}", ct);
        string baseTree = parent!["tree"]!["sha"]!.GetValue<string>();
        var tree = new JsonArray();
        foreach (var (path, text) in files)
            tree.Add(new JsonObject { ["path"] = path, ["mode"] = "100644", ["type"] = "blob", ["content"] = text });
        var newTree = await SendAsync(HttpMethod.Post, $"{RepoPath}/git/trees",
            new JsonObject { ["base_tree"] = baseTree, ["tree"] = tree }, ct);
        var commit = await SendAsync(HttpMethod.Post, $"{RepoPath}/git/commits", new JsonObject {
            ["message"] = message,
            ["tree"] = newTree!["sha"]!.GetValue<string>(),
            ["parents"] = new JsonArray(parentSha)
        }, ct);
        return commit!["sha"]!.GetValue<string>();
    }

    public async Task SetBranchAsync(string branch, string sha, CancellationToken ct) {
        var existing = await GetAsync($"{RepoPath}/git/ref/heads/{branch}", ct, allowMissing: true);
        if (existing is null)
            await SendAsync(HttpMethod.Post, $"{RepoPath}/git/refs",
                new JsonObject { ["ref"] = $"refs/heads/{branch}", ["sha"] = sha }, ct);
        else
            await SendAsync(HttpMethod.Patch, $"{RepoPath}/git/refs/heads/{branch}",
                new JsonObject { ["sha"] = sha, ["force"] = true }, ct);
    }

    public async Task<string> OpenPullAsync(string branch, string title, string body, CancellationToken ct) {
        var pr = await SendAsync(HttpMethod.Post, $"{RepoPath}/pulls",
            new JsonObject { ["title"] = title, ["head"] = branch, ["base"] = "main", ["body"] = body }, ct);
        return pr!["html_url"]!.GetValue<string>();
    }

    public async Task<(string State, string? TargetUrl)?> CommitStatusAsync(string sha, string context, CancellationToken ct) {
        var node = await GetAsync($"{RepoPath}/commits/{sha}/status", ct);
        foreach (var s in node?["statuses"]?.AsArray() ?? []) {
            if (s?["context"]?.GetValue<string>() != context) continue;
            return (s["state"]!.GetValue<string>(), s["target_url"]?.GetValue<string>());
        }

        return null;
    }

    private async Task<JsonNode?> GetAsync(string url, CancellationToken ct, bool allowMissing = false) {
        using var req = await RequestAsync(HttpMethod.Get, url, ct);
        using var resp = await http.SendAsync(req, ct);
        if (allowMissing && resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureAsync(resp, url, ct);
        return await resp.Content.ReadFromJsonAsync<JsonNode>(ct);
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string url, JsonObject body, CancellationToken ct) {
        using var req = await RequestAsync(method, url, ct);
        req.Content = JsonContent.Create(body);
        using var resp = await http.SendAsync(req, ct);
        await EnsureAsync(resp, url, ct);
        return await resp.Content.ReadFromJsonAsync<JsonNode>(ct);
    }

    private static async Task EnsureAsync(HttpResponseMessage resp, string url, CancellationToken ct) {
        if (resp.IsSuccessStatusCode) return;
        string text = await resp.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"GitHub {(int)resp.StatusCode} on {url}: {text}", null, resp.StatusCode);
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string url, CancellationToken ct) {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await InstallationTokenAsync(ct));
        Decorate(req);
        return req;
    }

    private static void Decorate(HttpRequestMessage req) {
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("eggincognito-sender", "1"));
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private async Task<string> InstallationTokenAsync(CancellationToken ct) {
        var now = time.GetUtcNow();
        if (_token is not null && now < _tokenExpires.AddMinutes(-5)) return _token;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Api}/app/installations/{config.InstallationId}/access_tokens");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AppJwt(now));
        Decorate(req);
        using var resp = await http.SendAsync(req, ct);
        await EnsureAsync(resp, req.RequestUri!.ToString(), ct);
        var node = await resp.Content.ReadFromJsonAsync<JsonNode>(ct);
        _token = node!["token"]!.GetValue<string>();
        _tokenExpires = node["expires_at"]!.GetValue<DateTimeOffset>();
        return _token;
    }

    private string AppJwt(DateTimeOffset now) {
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        string payload = B64(JsonSerializer.SerializeToUtf8Bytes(new {
            iat = now.AddSeconds(-60).ToUnixTimeSeconds(),
            exp = now.AddMinutes(9).ToUnixTimeSeconds(),
            iss = config.AppId
        }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(config.PrivateKeyPem);
        byte[] sig = rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return $"{header}.{payload}.{B64(sig)}";
    }
}
