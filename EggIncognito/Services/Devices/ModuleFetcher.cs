using System.Text.Json;
using EggIncognito.Core;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices;

public sealed record ModuleFetchResult(
    bool Ok, string Name, string? Version, byte[]? Bytes, long ByteSize, bool FromCache, string? Error);

public sealed class ModuleFetcher(
    IHttpClientFactory httpFactory,
    IServiceScopeFactory scopeFactory,
    VirtualDeviceConfig config,
    TimeProvider time,
    ILogger<ModuleFetcher> logger) {
    public const string HttpClientName = "device-modules";

    public async Task<ModuleFetchResult> ResolveAsync(IntegrityModuleSpec spec, bool forceRefresh, CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceModuleStore)) is not DeviceModuleStore store)
            return new ModuleFetchResult(false, spec.Name, null, null, 0, false, "no database configured");

        if (!spec.Pinned && !config.IntegrityAllowUnpinned) {
            return new ModuleFetchResult(false, spec.Name, null, null, 0, false,
                $"module '{spec.Name}' is not pinned (needs Tag + Sha256, or Url + Sha256); "
                + "refusing an unpinned root install (set Devices:Virtual:Integrity:AllowUnpinned to override)");
        }

        string? expected = Nz(spec.Sha256);
        var cached = await store.LatestAsync(spec.Name, ct);
        if (CacheUsable(cached, expected, forceRefresh))
            return new ModuleFetchResult(true, spec.Name, cached!.Version, cached.Bytes, cached.ByteSize, true, null);

        try {
            (byte[] bytes, string? version, string source) = await DownloadAsync(spec, ct);
            string sha = Hashes.Sha256Hex(bytes);
            if (expected is not null && !sha.Equals(expected, StringComparison.OrdinalIgnoreCase)) {
                return new ModuleFetchResult(false, spec.Name, version, null, 0, false,
                    $"checksum mismatch for '{spec.Name}': expected {expected}, got {sha}; "
                    + "refusing to cache or install");
            }

            await store.PutAsync(spec.Name, source, version, sha, bytes, ct);
            return new ModuleFetchResult(true, spec.Name, version, bytes, bytes.LongLength, false, null);
        } catch (Exception ex) {
            logger.LogWarning(ex, "module fetch: {Name} failed to resolve", spec.Name);
            if (CacheUsable(cached, expected, false))
                return new ModuleFetchResult(true, spec.Name, cached!.Version, cached.Bytes, cached.ByteSize, true, null);
            return new ModuleFetchResult(false, spec.Name, null, null, 0, false, ex.Message);
        }
    }

    private bool CacheUsable(StoredModule? cached, string? expected, bool forceRefresh) {
        if (cached is null) return false;
        if (expected is not null)
            return cached.Sha256.Equals(expected, StringComparison.OrdinalIgnoreCase);
        if (forceRefresh) return false;
        return time.GetUtcNow() - cached.FetchedAt < TimeSpan.FromHours(Math.Max(1, config.IntegrityRefreshHours));
    }

    private async Task<(byte[] Bytes, string? Version, string Source)> DownloadAsync(
        IntegrityModuleSpec spec, CancellationToken ct) {
        var http = httpFactory.CreateClient(HttpClientName);
        if (!string.IsNullOrWhiteSpace(spec.Url)) {
            byte[] direct = await http.GetByteArrayAsync(spec.Url, ct);
            return (direct, Nz(spec.Tag), spec.Url);
        }

        if (string.IsNullOrWhiteSpace(spec.Repo))
            throw new InvalidOperationException($"module '{spec.Name}' has neither a Url nor a Repo to resolve");
        if (string.IsNullOrWhiteSpace(spec.Tag))
            throw new InvalidOperationException($"module '{spec.Name}' has a Repo but no pinned Tag to fetch");

        (string assetUrl, string tag) = await ResolveTaggedAssetAsync(http, spec.Repo, spec.Tag, ".zip", ct);
        byte[] bytes = await http.GetByteArrayAsync(assetUrl, ct);
        return (bytes, tag, spec.Repo);
    }

    public async Task<ModuleFetchResult> ResolveAssetAsync(
        string name, string repo, string extension, bool forceRefresh, CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceModuleStore)) is not DeviceModuleStore store)
            return new ModuleFetchResult(false, name, null, null, 0, false, "no database configured");

        var cached = await store.LatestAsync(name, ct);
        if (CacheUsable(cached, null, forceRefresh))
            return new ModuleFetchResult(true, name, cached!.Version, cached.Bytes, cached.ByteSize, true, null);

        try {
            var http = httpFactory.CreateClient(HttpClientName);
            string tag = await LatestTagAsync(repo, ct)
                         ?? throw new InvalidOperationException($"{repo}: no latest release tag");
            (string assetUrl, _) = await ResolveTaggedAssetAsync(http, repo, tag, extension, ct);
            byte[] bytes = await http.GetByteArrayAsync(assetUrl, ct);
            await store.PutAsync(name, repo, tag, Hashes.Sha256Hex(bytes), bytes, ct);
            return new ModuleFetchResult(true, name, tag, bytes, bytes.LongLength, false, null);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "asset fetch: {Name} from {Repo} failed", name, repo);
            if (cached is not null)
                return new ModuleFetchResult(true, name, cached.Version, cached.Bytes, cached.ByteSize, true, null);
            return new ModuleFetchResult(false, name, null, null, 0, false, ex.Message);
        }
    }

    public async Task<string?> LatestTagAsync(string repo, CancellationToken ct) {
        try {
            var http = httpFactory.CreateClient(HttpClientName);
            using var resp = await http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest", ct);
            if (!resp.IsSuccessStatusCode) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        } catch (Exception ex) {
            logger.LogDebug(ex, "module fetch: latest-tag check for {Repo} failed", repo);
            return null;
        }
    }

    private static async Task<(string AssetUrl, string Tag)> ResolveTaggedAssetAsync(
        HttpClient http, string repo, string tag, string extension, CancellationToken ct) {
        string api = $"https://api.github.com/repos/{repo}/releases/tags/{Uri.EscapeDataString(tag)}";
        using var resp = await http.GetAsync(api, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"{repo}@{tag}: release has no assets");

        var zips = new List<(string Name, string Url)>();
        foreach (var asset in assets.EnumerateArray()) {
            string? name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is null || !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
            string? url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (url is { Length: > 0 }) zips.Add((name, url));
        }

        if (zips.Count == 0) throw new InvalidOperationException($"{repo}@{tag}: release has no {extension} asset");

        var release = zips.FirstOrDefault(z => !z.Name.Contains("debug", StringComparison.OrdinalIgnoreCase));
        return (release.Url ?? zips[0].Url, tag);
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
