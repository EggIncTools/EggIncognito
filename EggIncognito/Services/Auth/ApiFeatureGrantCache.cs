using System.Collections.Concurrent;

namespace EggIncognito.Services.Auth;

public sealed class ApiFeatureGrantCache(TimeProvider time) {
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<(int KeyId, string Feature), (string? State, DateTimeOffset At)> _entries = new();

    public async Task<string?> GetAsync(int keyId, string feature, Func<Task<string?>> load) {
        var now = time.GetUtcNow();
        if (_entries.TryGetValue((keyId, feature), out var hit) && now - hit.At < Ttl) return hit.State;
        string? state = await load();
        _entries[(keyId, feature)] = (state, now);
        return state;
    }

    public void Invalidate(int keyId) {
        foreach (var key in _entries.Keys.Where(k => k.KeyId == keyId)) _entries.TryRemove(key, out _);
    }
}
