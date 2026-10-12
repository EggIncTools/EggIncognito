using EggIncognito.Data.Services;

namespace EggIncognito.Services.Feed;

public sealed class FeedSampleSource(IServiceProvider services, FeedPublisher pages, ILogger<FeedSampleSource> logger) {
    public const int PerKind = 4;

    private readonly Dictionary<string, IReadOnlyList<FeedSample>> _cache = [];

    public async Task<IReadOnlyList<FeedSample>> ForAsync(NotificationKind kind, CancellationToken ct) {
        if (_cache.TryGetValue(kind.Key, out var cached)) return cached;
        IReadOnlyList<FeedSample> samples = [];
        if (services.GetService<EggIncognitoDbContext>() is { } db) {
            try {
                samples = await kind.LoadSamples(new FeedSampleContext(db, pages), ct);
            } catch (Exception ex) {
                logger.LogWarning(ex, "loading {Kind} notification samples failed", kind.Key);
            }
        }

        _cache[kind.Key] = samples;
        return samples;
    }

    public async Task<FeedSample?> FindAsync(NotificationKind kind, string? key, CancellationToken ct) {
        var samples = await ForAsync(kind, ct);
        return samples.FirstOrDefault(s => s.Key == key) ?? (samples.Count > 0 ? samples[0] : null);
    }
}
