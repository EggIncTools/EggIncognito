using System.Text.RegularExpressions;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.Assets;

namespace EggIncognito.Services.Assets;

public sealed partial class IconCdnOrigin(
    IHttpClientFactory httpFactory,
    ILogger<IconCdnOrigin> logger,
    TimeProvider time)
    : IGameAssetOrigin {
    private const string ArtifactsBase = AuxbrainHosts.Origin + "/dlc/artifacts/1/";

    [GeneratedRegex(@"^[A-Za-z0-9_-]+$", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex SafeAssetName();

    public bool CanHandle(GameAssetKey key) =>
        key.Kind == "icon"
        && (key.Name.StartsWith("afx_", StringComparison.Ordinal)
            || key.Name.StartsWith("egg_", StringComparison.Ordinal));

    public async Task<GameAsset?> FetchAsync(GameAssetKey key, CancellationToken ct) {
        if (string.IsNullOrEmpty(key.Name) || !SafeAssetName().IsMatch(key.Name)) return null;
        string url = ArtifactsBase + key.Name + ".png";
        try {
            var client = httpFactory.CreateClient("inspector");
            var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            byte[] bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0) return null;
            logger.LogInformation("cdn icon: fetched {Name} ({Bytes}B)", key.Name, bytes.Length);
            return new GameAsset(key, bytes, "image/png", $"cdn@auxbrain:{key.Name}", time.GetUtcNow());
        } catch (Exception ex) {
            logger.LogWarning(ex, "cdn icon fetch failed {Name}", key.Name);
            return null;
        }
    }
}
