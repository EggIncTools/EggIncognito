using EggIdentity.Filters;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Feed;

public sealed class FeedSampleSource(IServiceProvider services, FeedPublisher pages, ILogger<FeedSampleSource> logger) {
    public const int PerKind = 4;

    private readonly Dictionary<string, IReadOnlyList<FeedSample>> _samples = [];
    private readonly Dictionary<string, IReadOnlyList<FilterFieldDef>> _fields = [];

    public async Task<IReadOnlyList<FeedSample>> ForAsync(NotificationKind kind, CancellationToken ct) {
        if (_samples.TryGetValue(kind.Key, out var cached)) return cached;
        var samples = await LoadAsync(kind, kind.LoadSamples, [], ct);
        _samples[kind.Key] = samples;
        return samples;
    }

    public async Task<IReadOnlyList<FilterFieldDef>> FieldsAsync(NotificationKind kind, CancellationToken ct) {
        if (_fields.TryGetValue(kind.Key, out var cached)) return cached;
        IReadOnlyList<FilterFieldDef> fields = kind.Fields;
        if (kind.LoadOptions is { } load) {
            var options = await LoadAsync(kind, load, new Dictionary<string, IReadOnlyList<FilterOption>>(), ct);
            fields = [.. kind.Fields.Select(f => options.TryGetValue(f.Key, out var live) ? f with { Options = live } : f)];
        }

        _fields[kind.Key] = fields;
        return fields;
    }

    public async Task<FeedSample?> FindAsync(NotificationKind kind, string? key, CancellationToken ct) {
        var samples = await ForAsync(kind, ct);
        return samples.FirstOrDefault(s => s.Key == key) ?? (samples.Count > 0 ? samples[0] : null);
    }

    private async Task<T> LoadAsync<T>(NotificationKind kind, Func<FeedSampleContext, CancellationToken, Task<T>> load,
        T empty, CancellationToken ct) {
        if (services.GetService<EggIncognitoDbContext>() is not { } db) return empty;
        try {
            return await load(new FeedSampleContext(db, pages), ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "loading {Kind} notification data failed", kind.Key);
            return empty;
        }
    }
}
