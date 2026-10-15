namespace EggIncognito.Services.Predictions;

public abstract class TunedModelCache<T> {
    private readonly Lock _gate = new();
    private long _version = -1;
    private Task<T>? _value;

    public bool TryGet(long version, out T value) {
        lock (_gate) {
            if (_version == version && _value is { IsCompletedSuccessfully: true } done) {
                value = done.Result;
                return true;
            }
        }

        value = default!;
        return false;
    }

    public void Set(long version, T value) {
        lock (_gate) {
            _version = version;
            _value = Task.FromResult(value);
        }
    }

    public Task<T> GetOrAddAsync(long version, Func<Task<T>> build) {
        lock (_gate) {
            if (_version == version && _value is { IsFaulted: false, IsCanceled: false } pending) return pending;
            _version = version;
            _value = Task.Run(build);
            return _value;
        }
    }
}
